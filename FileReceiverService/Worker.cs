using Azure.Identity;
using Azure.Messaging.ServiceBus;
using System.Text.Json;

namespace FileReceiverService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusProcessor _serviceBusProcessor;
    private readonly string _receiveFolder;

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        var fullyQualifiedNamespace = configuration["ServiceBusConfig:FullyQualifiedNamespace"];
        var queueName = configuration["ServiceBusConfig:QueueName"];
        var receiveFolder = configuration["ServiceConfig:ReceiveFolder"];
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

        if (string.IsNullOrEmpty(receiveFolder))
        {
            _logger.LogError("ServiceConfig:ReceiveFolder is not configured");
            throw new ArgumentException("Receive folder path is required");
        }

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            _logger.LogError("Azure AD configuration is incomplete");
            throw new ArgumentException("Azure AD configuration is required");
        }

        _receiveFolder = receiveFolder;

        // Create a ServiceBusClient using Azure AD authentication
        var credential = new ClientSecretCredential(
            tenantId,
            clientId,
            clientSecret
        );

        _serviceBusClient = new ServiceBusClient(fullyQualifiedNamespace, credential);
        
        var processorOptions = new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 1,
            AutoCompleteMessages = false
        };

        _serviceBusProcessor = _serviceBusClient.CreateProcessor(queueName, processorOptions);
        _serviceBusProcessor.ProcessMessageAsync += ProcessMessageAsync;
        _serviceBusProcessor.ProcessErrorAsync += ProcessErrorAsync;

        _logger.LogInformation("Worker service initialized with queue {QueueName}", queueName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Ensure receive folder exists
            if (!Directory.Exists(_receiveFolder))
            {
                Directory.CreateDirectory(_receiveFolder);
                _logger.LogInformation("Created receive folder at {ReceiveFolder}", _receiveFolder);
            }

            // Start processing messages
            await _serviceBusProcessor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("Started processing messages from Service Bus queue");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }

            // Stop processing
            await _serviceBusProcessor.StopProcessingAsync(stoppingToken);
            _logger.LogInformation("Stopped processing messages from Service Bus queue");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while processing messages");
            throw;
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var operationId = args.Message.ApplicationProperties.TryGetValue("OperationId", out var opId) 
            ? opId.ToString() 
            : Guid.NewGuid().ToString();

        var fileName = args.Message.ApplicationProperties.TryGetValue("FileName", out var fn)
            ? fn.ToString()
            : "unknown.txt";

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["OperationId"] = operationId,
            ["FileName"] = fileName,
            ["MessageId"] = args.Message.MessageId
        });

        try
        {
            _logger.LogInformation("Started processing message {MessageId} for file {FileName} with operation {OperationId}", 
                args.Message.MessageId, 
                fileName, 
                operationId);

            // Parse message body
            var messageBody = args.Message.Body.ToString();
            var fileData = JsonSerializer.Deserialize<FileData>(messageBody);

            if (fileData == null)
            {
                throw new InvalidOperationException("Message body could not be deserialized");
            }

            // Ensure receive folder exists
            if (!Directory.Exists(_receiveFolder))
            {
                Directory.CreateDirectory(_receiveFolder);
                _logger.LogInformation("Created receive folder at {ReceiveFolder}", _receiveFolder);
            }

            // Create file path
            var filePath = Path.Combine(_receiveFolder, fileName);

            // Write content to file
            await File.WriteAllTextAsync(filePath, fileData.Content);

            _logger.LogInformation(
                "Successfully processed message {MessageId} and created file {FilePath} with operation {OperationId}", 
                args.Message.MessageId,
                filePath,
                operationId);

            // Complete the message
            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error processing message {MessageId} for file {FileName} with operation {OperationId}", 
                args.Message.MessageId,
                fileName,
                operationId);

            // Abandon the message to retry later
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Error occurred while processing messages: {ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping file receiver service");

        if (_serviceBusProcessor != null)
        {
            await _serviceBusProcessor.DisposeAsync();
            _logger.LogInformation("Service Bus processor disposed");
        }

        if (_serviceBusClient != null)
        {
            await _serviceBusClient.DisposeAsync();
            _logger.LogInformation("Service Bus client disposed");
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("File receiver service stopped");
    }
}

public class FileData
{
    public string OperationId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime ProcessedTime { get; set; }
    public long FileSize { get; set; }
}
