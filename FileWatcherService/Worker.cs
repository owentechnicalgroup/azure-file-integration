using Azure.Identity;
using Azure.Messaging.ServiceBus;
using System.Text.Json;
using System.Collections.Concurrent;

namespace FileWatcherService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private FileSystemWatcher? _watcher;
    private readonly ServiceBusClient? _serviceBusClient;
    private readonly ServiceBusSender _serviceBusSender;
    private readonly string _processedFolder;
    private readonly ConcurrentDictionary<string, bool> _processingFiles = new();

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        var fullyQualifiedNamespace = configuration["ServiceBusConfig:FullyQualifiedNamespace"];
        var queueName = configuration["ServiceBusConfig:QueueName"];
        var watchFolder = configuration["ServiceConfig:WatchFolder"];
        var tenantId = configuration["AzureAd:TenantId"];
        var clientId = configuration["AzureAd:ClientId"];
        var clientSecret = configuration["AzureAd:ClientSecret"];
        
        if (string.IsNullOrEmpty(fullyQualifiedNamespace))
        {
            _logger.LogError("ServiceBusConfig:FullyQualifiedNamespace is not configured");
            throw new ArgumentException("Service Bus namespace is required");
        }

        if (string.IsNullOrEmpty(queueName))
        {
            _logger.LogError("ServiceBusConfig:QueueName is not configured");
            throw new ArgumentException("Service Bus queue name is required");
        }

        if (string.IsNullOrEmpty(watchFolder))
        {
            _logger.LogError("ServiceConfig:WatchFolder is not configured");
            throw new ArgumentException("Watch folder path is required");
        }

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            _logger.LogError("Azure AD configuration is incomplete");
            throw new ArgumentException("Azure AD configuration is required");
        }

        _processedFolder = Path.Combine(watchFolder, "processed");

        // Create a ServiceBusClient using Azure AD authentication
        var credential = new ClientSecretCredential(
            tenantId,
            clientId,
            clientSecret
        );

        _serviceBusClient = new ServiceBusClient(fullyQualifiedNamespace, credential);
        _serviceBusSender = _serviceBusClient.CreateSender(queueName);
        
        _logger.LogInformation("Worker service initialized with queue {QueueName}", queueName);
    }

    // Constructor for testing
    public Worker(ILogger<Worker> logger, IConfiguration configuration, ServiceBusSender serviceBusSender)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceBusSender = serviceBusSender ?? throw new ArgumentNullException(nameof(serviceBusSender));
        
        var watchFolder = configuration["ServiceConfig:WatchFolder"];
        if (string.IsNullOrEmpty(watchFolder))
        {
            throw new ArgumentException("Watch folder path is required");
        }
        _processedFolder = Path.Combine(watchFolder, "processed");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var watchFolder = _configuration["ServiceConfig:WatchFolder"];
        if (string.IsNullOrEmpty(watchFolder))
        {
            throw new ArgumentException("Watch folder path is required");
        }

        var fileFilter = _configuration["ServiceConfig:FileFilter"] ?? "*.*";

        // Ensure watch folder exists
        Directory.CreateDirectory(watchFolder);
        _logger.LogInformation("Created watch folder at {WatchFolder}", watchFolder);

        // Create processed folder
        Directory.CreateDirectory(_processedFolder);
        _logger.LogInformation("Created processed folder at {ProcessedFolder}", _processedFolder);

        _watcher = new FileSystemWatcher(watchFolder, fileFilter)
        {
            NotifyFilter = NotifyFilters.Attributes
                             | NotifyFilters.CreationTime
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.FileName
                             | NotifyFilters.LastAccess
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Security
                             | NotifyFilters.Size
        };

        _watcher.Created += OnFileCreated;
        _watcher.EnableRaisingEvents = true;

        _logger.LogInformation("File watcher service started at {StartTime} watching folder {WatchFolder} with filter {FileFilter}", 
            DateTimeOffset.Now, 
            watchFolder,
            fileFilter);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    private async void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        var fileName = Path.GetFileName(e.FullPath);
        
        // Check if file is already being processed
        if (!_processingFiles.TryAdd(fileName, true))
        {
            _logger.LogInformation("File {FileName} is already being processed", fileName);
            return;
        }

        var operationId = Guid.NewGuid().ToString();
        
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["OperationId"] = operationId,
            ["FileName"] = fileName,
            ["FilePath"] = e.FullPath
        });

        try
        {
            _logger.LogInformation("Started processing file {FileName} with operation {OperationId}", fileName, operationId);

            // Wait briefly to ensure file is completely written
            await Task.Delay(1000);

            var fileInfo = new FileInfo(e.FullPath);
            _logger.LogInformation("File details: Size={FileSize}bytes, CreationTime={CreationTime}", 
                fileInfo.Length,
                fileInfo.CreationTime);

            // Read and process the file
            var fileContent = await File.ReadAllTextAsync(e.FullPath);
            
            // Create a JSON payload
            var payload = new
            {
                OperationId = operationId,
                FileName = fileName,
                Content = fileContent,
                ProcessedTime = DateTime.UtcNow,
                FileSize = fileInfo.Length
            };

            // Serialize to JSON
            var jsonPayload = JsonSerializer.Serialize(payload);

            // Send to Service Bus
            var message = new ServiceBusMessage(jsonPayload);
            message.ApplicationProperties.Add("OperationId", operationId);
            message.ApplicationProperties.Add("FileName", fileName);
            
            await _serviceBusSender.SendMessageAsync(message);

            // Move file to processed folder with timestamp
            var processedFilePath = Path.Combine(_processedFolder, $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(fileName)}");
            File.Move(e.FullPath, processedFilePath);

            _logger.LogInformation("Successfully processed file {FileName} and moved to {ProcessedPath} with operation {OperationId}", 
                fileName, 
                processedFilePath,
                operationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file {FileName} with operation {OperationId}", fileName, operationId);
        }
        finally
        {
            _processingFiles.TryRemove(fileName, out _);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping file watcher service");

        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileCreated;
            _watcher.Dispose();
            _logger.LogInformation("File watcher disposed");
        }

        if (_serviceBusSender != null)
        {
            await _serviceBusSender.DisposeAsync();
            _logger.LogInformation("Service Bus sender disposed");
        }

        if (_serviceBusClient != null)
        {
            await _serviceBusClient.DisposeAsync();
            _logger.LogInformation("Service Bus client disposed");
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("File watcher service stopped");
    }
}
