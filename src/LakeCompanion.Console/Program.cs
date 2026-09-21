using LakeCompanion.Application;
using LakeCompanion.ConsoleApp;
using LakeCompanion.Domain;
using LakeCompanion.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

System.Console.OutputEncoding = System.Text.Encoding.UTF8;
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddLakeInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ICompanionService, CompanionService>();
builder.Services.AddSingleton<FishingGame>();
builder.Services.AddSingleton<LakeMap>();
builder.Services.AddSingleton<CompanionAmbientScheduler>();
builder.Services.AddSingleton<Random>();
builder.Services.AddSingleton<JsonSaveRepository>();

using var host = builder.Build();
using var cancellation = new CancellationTokenSource();

var fishing = host.Services.GetRequiredService<FishingGame>();
var companionService = host.Services.GetRequiredService<ICompanionService>();
var world = host.Services.GetRequiredService<LakeMap>();
var ambientScheduler = host.Services.GetRequiredService<CompanionAmbientScheduler>();
var saves = host.Services.GetRequiredService<JsonSaveRepository>();
var sqliteSaves = host.Services.GetRequiredService<SqliteSaveRepository>();
var music = host.Services.GetRequiredService<IAmbientMusicService>();
var voice = host.Services.GetRequiredService<IVoiceInputService>();
var speech = host.Services.GetRequiredService<ISpeechSynthesizer>();
var speechRuntime = host.Services.GetRequiredService<SpeechService>();
var companionSpeechEnabled = bool.TryParse(builder.Configuration["CompanionSpeech:Enabled"], out var configuredSpeechEnabled)
    && configuredSpeechEnabled;
var exitRequested = 0;
System.Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    if (Interlocked.Exchange(ref exitRequested, 1) != 0)
    {
        return;
    }

    cancellation.Cancel();
    _ = music.StopAsync(CancellationToken.None);
};
voice.StatusChanged += message => System.Console.WriteLine($"  {message}");
speechRuntime.NarrationFailed += message => System.Console.WriteLine($"\n  {message}");
speechRuntime.NarrationStatusChanged += message => System.Console.WriteLine($"\n  {message}");
GameSaveState? savedState;
try
{
    savedState = await saves.LoadAsync(CancellationToken.None);
}
catch (InvalidDataException)
{
    savedState = null;
}

var character = savedState is null ? new Character("Mira") : Character.Restore(savedState.Character);
var companion = savedState is null ? new Companion() : Companion.Restore(savedState.Companion);
var positionRestored = savedState is not null && world.TryRestore(savedState.World);
await music.StartAsync(CancellationToken.None);
var notice = savedState is not null && positionRestored
    ? $"С возвращением. Ты снова у места «{world.CurrentLocation.Name}»."
    : music.IsPlaying ? "Рован ставит рядом ящик со снастями. «Не спеши». Над озером звучит тихая музыка." : music.Status;
string? reaction = null;
string? ambientReaction = null;
var ambientMovesRemaining = 0;

async Task SaveProgressAsync()
{
    var state = new GameSaveState(1, DateTimeOffset.UtcNow, character.ToSnapshot(), companion.ToSnapshot(), world.ToSnapshot());
    await saves.SaveAsync(state, CancellationToken.None);
    await sqliteSaves.SaveAsync(character, companion, CancellationToken.None);
}

try
{
    while (!cancellation.IsCancellationRequested)
    {
        RenderScreen(world, character, companion, notice, reaction ?? ambientReaction, music.Status, companionSpeechEnabled);
        System.Console.Write("\n  Ходьба: стрелки/WASD   Действия: [f]рыбалка [t]разговор [v]голос [o]голос Рована [r]отдых [m]музыка [q]выход > ");
        var key = (await Task.Factory.StartNew(
                () => System.Console.ReadKey(intercept: true),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
            .WaitAsync(cancellation.Token)).Key;
        reaction = null;

        if (world.TryMove(key, out var movement))
        {
            notice = movement;
            if (!movement.StartsWith("Берег", StringComparison.Ordinal))
            {
                if (ambientMovesRemaining > 0)
                {
                    ambientMovesRemaining--;
                }

                if (ambientScheduler.RecordMove())
                {
                    companion.Mood = CompanionMood.Curious;
                    reaction = await GetReactionAsync(companionService, speech, companion, $"Вы вместе пришли к месту «{world.CurrentLocation.Name}».", companionSpeechEnabled, cancellation.Token);
                    ambientReaction = reaction;
                    // The triggering movement displays it immediately; retain it for ten more successful moves.
                    ambientMovesRemaining = 11;
                }

                await SaveProgressAsync();
            }
            continue;
        }

        switch (key)
        {
        case ConsoleKey.F:
        {
            var result = await fishing.PlayAsync(character.FishingLevel, cancellation.Token);
            if (result.Catch is { } catchResult)
            {
                character.Inventory.Add(catchResult);
                character.GainExperience(catchResult.Species.ExperienceAward);
                companion.Mood = CompanionMood.Excited;
                notice = $"Ты поймал {catchResult.Species.Name} — {catchResult.WeightGrams} г! Уровень рыбалки: {character.FishingLevel}.";
            }
            else
            {
                companion.Mood = CompanionMood.Curious;
                notice = result.Quality is CatchQuality.Small ? "Рыба сорвалась с крючка." : "Круги на воде расходятся. Поклёвки не было.";
            }
            await SaveProgressAsync();
            break;
        }
        case ConsoleKey.T:
            System.Console.Write("\n\n  Ты Ровану > ");
            var playerMessage = System.Console.ReadLine();
            if (string.IsNullOrWhiteSpace(playerMessage))
            {
                notice = "Ты решаешь просто разделить тишину.";
            }
            else
            {
                companion.Relationship.Adjust(2);
                notice = "Рован думает...";
                RenderScreen(world, character, companion, notice, null, music.Status, companionSpeechEnabled);
                reaction = await GetReactionAsync(
                    companionService,
                    speech,
                    companion,
                    $"В месте «{world.CurrentLocation.Name}» игрок говорит: «{playerMessage.Trim()}».",
                    companionSpeechEnabled,
                    cancellation.Token);
                notice = "Ты поговорил с Рованом. Отношения +2.";
            }
            await SaveProgressAsync();
            break;
        case ConsoleKey.R:
            companion.Mood = CompanionMood.Content;
            notice = "Камыши шепчут. Вы вместе смотрите, как на воде меняется свет.";
            await SaveProgressAsync();
            break;
        case ConsoleKey.V:
            System.Console.WriteLine("\n  Говори. Локальная запись займёт не больше 8 секунд...");
            try
            {
                var voiceResult = await voice.CaptureAndTranscribeAsync(cancellation.Token);
                if (string.IsNullOrWhiteSpace(voiceResult.Transcript))
                {
                    notice = voiceResult.FailureMessage ?? "Речь не распознана.";
                }
                else
                {
                    companion.Relationship.Adjust(2);
                    notice = $"Ты сказал: «{voiceResult.Transcript}» Отношения +2.";
                    notice = "Рован думает...";
                    RenderScreen(world, character, companion, notice, null, music.Status, companionSpeechEnabled);
                    reaction = await GetReactionAsync(companionService, speech, companion, $"В месте «{world.CurrentLocation.Name}» игрок говорит: «{voiceResult.Transcript}».", companionSpeechEnabled, cancellation.Token);
                    notice = $"Ты сказал: «{voiceResult.Transcript}» Отношения +2.";
                    await SaveProgressAsync();
                }
            }
            catch (Exception)
            {
                notice = "Голосовой ввод недоступен. Проверь ffmpeg и локальную модель Whisper.";
            }
            break;
        case ConsoleKey.O:
            companionSpeechEnabled = !companionSpeechEnabled;
            notice = companionSpeechEnabled
                ? "Озвучка Рована включена. Следующий ответ прозвучит вслух."
                : "Озвучка Рована выключена. Его ответы останутся текстовыми.";
            if (companionSpeechEnabled)
            {
                StartNarration(speech, "Озвучка Рована включена.", cancellation.Token);
            }
            break;
        case ConsoleKey.M:
            if (music.IsPaused)
            {
                await music.ResumeAsync(CancellationToken.None);
            }
            else if (music.IsPlaying)
            {
                await music.PauseAsync(CancellationToken.None);
            }
            else
            {
                await music.StartAsync(CancellationToken.None);
            }

            notice = music.Status;
            break;
        case ConsoleKey.Q:
            System.Console.Clear();
            System.Console.WriteLine("  Дневник сохранён. Озеро будет ждать тебя завтра.");
            System.Console.WriteLine($"  Файл сохранения: {saves.SavePath}");
            return;
        default:
            notice = "Ходи стрелками или WASD; F, T, V, O, R, M и Q — действия.";
            break;
        }
    }
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    // Rider or Ctrl+C requested a graceful shutdown.
}
finally
{
    cancellation.Cancel();
    await music.StopAsync(CancellationToken.None);
    await SaveProgressAsync();
}

static async Task<string> GetReactionAsync(
    ICompanionService service,
    ISpeechSynthesizer speech,
    Companion companion,
    string action,
    bool speechEnabled,
    CancellationToken cancellationToken)
{
    var response = await service.ReactAsync(companion, action, cancellationToken);
    if (speechEnabled && !response.IsSilent)
    {
        StartNarration(speech, response.Text, cancellationToken);
    }

    return $"{DisplayCompanionName(companion)}: «{response.Text}»";
}

static async Task SpeakReplyAsync(ISpeechSynthesizer speech, string text, CancellationToken gameCancellationToken)
{
    try
    {
        await speech.SpeakAsync(text, gameCancellationToken);
    }
    catch (OperationCanceledException)
    {
        // The reply was superseded, the game exited, or local narration exceeded its hard limit.
    }
    catch (Exception)
    {
        // Speech is optional; the text reply remains available when the local voice is unavailable.
    }
}

static void StartNarration(ISpeechSynthesizer speech, string text, CancellationToken cancellationToken)
{
    _ = Task.Factory.StartNew(
            () => SpeakReplyAsync(speech, text, cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)
        .Unwrap();
}

static void RenderScreen(LakeMap world, Character character, Companion companion, string notice, string? reaction, string musicStatus, bool companionSpeechEnabled)
{
    if (!System.Console.IsOutputRedirected)
    {
        System.Console.Clear();
    }

    System.Console.WriteLine("\u001b[36m╭──────────────── Спутник у озера ───────────────╮\u001b[0m");
    System.Console.WriteLine($"\u001b[36m│  {world.CurrentLocation.Name,-46}│\u001b[0m");
    System.Console.WriteLine("\u001b[36m╰────────────────────────────────────────────────╯\u001b[0m");
    world.Render();
    System.Console.WriteLine($"\n  @ {character.Name} · уровень рыбалки {character.FishingLevel} ({character.Experience}/{character.FishingLevel * 100} опыта)");
    System.Console.WriteLine($"  & {DisplayCompanionName(companion)} · {DisplayMood(companion.Mood)} · отношения {companion.Relationship.Score:+#;-#;0}");
    System.Console.WriteLine($"  Голос Рована · {(companionSpeechEnabled ? "ВКЛ" : "ВЫКЛ")} · O — переключить");
    System.Console.WriteLine($"  Музыка · {musicStatus}");
    System.Console.WriteLine($"\n  {world.CurrentLocation.Description}");
    System.Console.WriteLine($"  {notice}");
    if (reaction is not null)
    {
        System.Console.WriteLine($"\n  {reaction}");
    }
}

static string DisplayCompanionName(Companion companion) => companion.Name == "Rowan" ? "Рован" : companion.Name;

static string DisplayMood(CompanionMood mood) => mood switch
{
    CompanionMood.Content => "спокоен",
    CompanionMood.Curious => "заинтересован",
    CompanionMood.Excited => "воодушевлён",
    CompanionMood.Concerned => "внимателен",
    CompanionMood.Quiet => "молчит",
    _ => "спокоен"
};
