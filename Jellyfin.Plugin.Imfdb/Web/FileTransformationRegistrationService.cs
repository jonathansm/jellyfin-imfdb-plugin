using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Imfdb.Web;

/// <summary>
/// Registers Jellyfin Web transformations with the File Transformation plugin.
/// </summary>
public class FileTransformationRegistrationService : IHostedService
{
    private static readonly Guid TransformationId = Guid.Parse("5c67db76-0120-4636-a557-6d74cdaac5a7");
    private static readonly string[] FileNamePatterns =
    {
        "index.html",
        "web/index.html",
        "/web/index.html",
        "index\\.html$",
        "^index\\.html$",
        "web/index\\.html$",
        "^web/index\\.html$",
        "/web/index\\.html$"
    };

    private readonly ILogger<FileTransformationRegistrationService> _logger;
    private CancellationTokenSource? _stoppingTokenSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTransformationRegistrationService"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public FileTransformationRegistrationService(ILogger<FileTransformationRegistrationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether the transformation registered successfully.
    /// </summary>
    public static bool IsRegistered { get; private set; }

    /// <summary>
    /// Gets the most recent registration status message.
    /// </summary>
    public static string LastStatus { get; private set; } = "Not started.";

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = RegisterWithRetryAsync(_stoppingTokenSource.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stoppingTokenSource?.Cancel();
        _stoppingTokenSource?.Dispose();
        return Task.CompletedTask;
    }

    private async Task RegisterWithRetryAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 6;
        for (var attempt = 1; attempt <= maxAttempts && !cancellationToken.IsCancellationRequested; attempt++)
        {
            if (TryRegisterTransformation())
            {
                return;
            }

            if (attempt < maxAttempts)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private bool TryRegisterTransformation()
    {
        try
        {
            var fileTransformationAssembly = AssemblyLoadContext.All
                .SelectMany(static context => context.Assemblies)
                .FirstOrDefault(static assembly => assembly.FullName?.Contains(".FileTransformation", StringComparison.OrdinalIgnoreCase) == true);

            if (fileTransformationAssembly is null)
            {
                LastStatus = "File Transformation plugin is not loaded.";
                _logger.LogInformation("{Status}", LastStatus);
                return false;
            }

            var pluginInterfaceType = fileTransformationAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
            var registerMethod = pluginInterfaceType?.GetMethod("RegisterTransformation", BindingFlags.Public | BindingFlags.Static);
            var payloadType = registerMethod?.GetParameters().FirstOrDefault()?.ParameterType;
            var parseMethod = payloadType?.GetMethod("Parse", new[] { typeof(string) });

            if (registerMethod is null || payloadType is null || parseMethod is null)
            {
                LastStatus = "File Transformation plugin was found, but the registration API was not available.";
                _logger.LogWarning("{Status}", LastStatus);
                return false;
            }

            foreach (var fileNamePattern in FileNamePatterns)
            {
                var payloadJson = $$"""
                    {
                      "id": "{{GuidFromPattern(fileNamePattern)}}",
                      "fileNamePattern": "{{fileNamePattern.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                      "callbackAssembly": "{{typeof(ImfdbWebTransformer).Assembly.FullName}}",
                      "callbackClass": "{{typeof(ImfdbWebTransformer).FullName}}",
                      "callbackMethod": "{{nameof(ImfdbWebTransformer.TransformIndex)}}"
                    }
                    """;

                var payload = parseMethod.Invoke(null, new object[] { payloadJson });
                registerMethod.Invoke(null, new[] { payload });
            }

            IsRegistered = true;
            LastStatus = "Registered IMFDB Jellyfin Web transformation with File Transformation.";
            _logger.LogInformation("{Status}", LastStatus);
            return true;
        }
        catch (Exception ex)
        {
            LastStatus = "Failed to register IMFDB Jellyfin Web transformation: " + ex.GetType().Name + " " + ex.Message;
            _logger.LogWarning(ex, "{Status}", LastStatus);
            return false;
        }
    }

    private static Guid GuidFromPattern(string fileNamePattern)
    {
        var bytes = TransformationId.ToByteArray();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(fileNamePattern));
        Array.Copy(hashBytes, 0, bytes, 12, bytes.Length - 12);
        return new Guid(bytes);
    }
}
