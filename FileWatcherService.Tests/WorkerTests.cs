using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Azure.Messaging.ServiceBus;

namespace FileWatcherService.Tests;

public class WorkerTests : IDisposable
{
    private readonly Mock<ILogger<Worker>> _loggerMock;
    private readonly Mock<IConfiguration> _configMock;
    private readonly Mock<ServiceBusSender> _serviceBusSenderMock;
    private readonly string _testWatchFolder;
    private readonly Worker _worker;

    public WorkerTests()
    {
        _loggerMock = new Mock<ILogger<Worker>>();
        _configMock = new Mock<IConfiguration>();
        _serviceBusSenderMock = new Mock<ServiceBusSender>();

        // Setup test watch folder
        _testWatchFolder = Path.Combine(Path.GetTempPath(), "TestWatchFolder");
        Directory.CreateDirectory(_testWatchFolder);

        // Setup configuration mock
        _configMock.Setup(x => x["ServiceConfig:WatchFolder"]).Returns(_testWatchFolder);
        _configMock.Setup(x => x["ServiceConfig:FileFilter"]).Returns("*.txt");
        _configMock.Setup(x => x["ServiceBusConfig:QueueName"]).Returns("test-queue");

        // Create worker with mocked ServiceBusSender
        _worker = new Worker(_loggerMock.Object, _configMock.Object, _serviceBusSenderMock.Object);
    }

    [Fact]
    public async Task Worker_Initializes_Successfully()
    {
        // Arrange
        var cancellationTokenSource = new CancellationTokenSource();

        try
        {
            // Act
            await _worker.StartAsync(cancellationTokenSource.Token);

            // Assert
            Assert.True(Directory.Exists(_testWatchFolder));
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("File watcher service started")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
        finally
        {
            await _worker.StopAsync(cancellationTokenSource.Token);
            cancellationTokenSource.Cancel();
        }
    }

    [Fact]
    public async Task Worker_Processes_New_File()
    {
        // Arrange
        var cancellationTokenSource = new CancellationTokenSource();
        var testContent = "Test file content";
        var testFilePath = Path.Combine(_testWatchFolder, "test.txt");

        _serviceBusSenderMock
            .Setup(x => x.SendMessageAsync(
                It.IsAny<ServiceBusMessage>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        try
        {
            // Start the worker
            await _worker.StartAsync(cancellationTokenSource.Token);

            // Act
            await File.WriteAllTextAsync(testFilePath, testContent);

            // Wait for file processing
            await Task.Delay(2000); // Give enough time for the file to be processed

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Processing new file")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            _serviceBusSenderMock.Verify(
                x => x.SendMessageAsync(
                    It.Is<ServiceBusMessage>(m => 
                        m.Body.ToString().Contains(testContent)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            await _worker.StopAsync(cancellationTokenSource.Token);
            cancellationTokenSource.Cancel();
            if (File.Exists(testFilePath))
            {
                File.Delete(testFilePath);
            }
        }
    }

    [Fact]
    public async Task Worker_Handles_Invalid_Configuration()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<Worker>>();
        var configMock = new Mock<IConfiguration>();
        var senderMock = new Mock<ServiceBusSender>();

        // Setup configuration mock with invalid values
        configMock.Setup(x => x["ServiceConfig:WatchFolder"]).Returns((string)null);

        var worker = new Worker(loggerMock.Object, configMock.Object, senderMock.Object);
        var cancellationTokenSource = new CancellationTokenSource();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await worker.StartAsync(cancellationTokenSource.Token);
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_testWatchFolder))
        {
            Directory.Delete(_testWatchFolder, true);
        }
    }
}
