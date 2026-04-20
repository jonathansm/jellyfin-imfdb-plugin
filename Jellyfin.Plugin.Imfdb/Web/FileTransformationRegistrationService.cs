using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Imfdb.Web;

/// <summary>
/// Registers Jellyfin Web transformations with the File Transformation plugin.
/// </summary>
public class FileTransformationRegistrationService : IHostedService
{
    private static readonly Guid TransformationId = Guid.Parse("5c67db76-0120-4636-a557-6d74cdaac5a7");
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
                _logger.LogInformation("File Transformation plugin is not loaded; IMFDB Jellyfin Web row injection is disabled.");
                return false;
            }

            var pluginInterfaceType = fileTransformationAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
            var registerMethod = pluginInterfaceType?.GetMethod("RegisterTransformation", BindingFlags.Public | BindingFlags.Static);
            var payloadType = registerMethod?.GetParameters().FirstOrDefault()?.ParameterType;
            var parseMethod = payloadType?.GetMethod("Parse", new[] { typeof(string) });

            if (registerMethod is null || payloadType is null || parseMethod is null)
            {
                _logger.LogWarning("File Transformation plugin was found, but the registration API was not available.");
                return false;
            }

            var payloadJson = $$"""
                {
                  "id": "{{TransformationId}}",
                  "fileNamePattern": "index\\.html$",
                  "callbackAssembly": "{{typeof(ImfdbWebTransformer).Assembly.FullName}}",
                  "callbackClass": "{{typeof(ImfdbWebTransformer).FullName}}",
                  "callbackMethod": "{{nameof(ImfdbWebTransformer.TransformIndex)}}"
                }
                """;

            var payload = parseMethod.Invoke(null, new object[] { payloadJson });
            registerMethod.Invoke(null, new[] { payload });
            _logger.LogInformation("Registered IMFDB Jellyfin Web transformation with File Transformation.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register IMFDB Jellyfin Web transformation.");
            return false;
        }
    }
}
