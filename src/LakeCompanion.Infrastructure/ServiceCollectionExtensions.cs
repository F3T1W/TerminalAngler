using LakeCompanion.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace LakeCompanion.Infrastructure;

/// <summary>Registers infrastructure implementations while keeping the game loop independent of providers.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds local-only Ollama and speech service implementations.</summary>
    public static IServiceCollection AddLakeInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));
        services.Configure<AmbientMusicOptions>(configuration.GetSection(AmbientMusicOptions.SectionName));
        services.Configure<VoiceOptions>(configuration.GetSection(VoiceOptions.SectionName));
        services.Configure<CompanionSpeechOptions>(configuration.GetSection(CompanionSpeechOptions.SectionName));
        services.AddHttpClient<ILocalLanguageModel, OllamaClient>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddSingleton<SpeechService>();
        services.AddSingleton<ISpeechRecognizer>(provider => provider.GetRequiredService<SpeechService>());
        services.AddSingleton<ISpeechSynthesizer>(provider => provider.GetRequiredService<SpeechService>());
        services.AddSingleton<IVoiceInputService, VoiceInputService>();
        services.AddSingleton<IAmbientMusicService, AmbientMusicService>();
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LakeCompanion");
        Directory.CreateDirectory(dataDirectory);
        services.AddDbContextFactory<LakeCompanionDbContext>(options =>
            options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "lake-companion.db")}"));
        services.AddSingleton<SqliteSaveRepository>();
        return services;
    }
}
