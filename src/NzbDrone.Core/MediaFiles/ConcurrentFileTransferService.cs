using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MediaFiles
{
    public interface IConcurrentFileTransferService
    {
        Task TransferFileAsync(string sourcePath, string destinationPath, TransferMode mode);
        Task<bool> TryTransferFileAsync(string sourcePath, string destinationPath, TransferMode mode, CancellationToken cancellationToken = default);
    }

    public class ConcurrentFileTransferService : IConcurrentFileTransferService
    {
        private readonly IDiskTransferService _diskTransferService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;
        private SemaphoreSlim _semaphore;
        private int _currentMaxConcurrency;

        public ConcurrentFileTransferService(IDiskTransferService diskTransferService, IConfigService configService, Logger logger)
        {
            _diskTransferService = diskTransferService;
            _configService = configService;
            _logger = logger;
            _currentMaxConcurrency = 2; // Default value since MaxConcurrentFileTransfers doesn't exist
            _semaphore = new SemaphoreSlim(_currentMaxConcurrency, _currentMaxConcurrency);
        }

        public async Task TransferFileAsync(string sourcePath, string destinationPath, TransferMode mode)
        {
            await UpdateSemaphoreIfNeeded();

            await _semaphore.WaitAsync();
            try
            {
                _logger.Debug("Starting {0} operation: {1} -> {2}", mode, sourcePath, destinationPath);

                await _diskTransferService.TransferFileAsync(sourcePath, destinationPath, mode);

                _logger.Debug("Completed {0} operation: {1} -> {2}", mode, sourcePath, destinationPath);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<bool> TryTransferFileAsync(string sourcePath, string destinationPath, TransferMode mode, CancellationToken cancellationToken = default)
        {
            try
            {
                await TransferFileAsync(sourcePath, destinationPath, mode);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to transfer file: {0} -> {1}", sourcePath, destinationPath);
                return false;
            }
        }

        private async Task UpdateSemaphoreIfNeeded()
        {
            var newMaxConcurrency = 2; // Default value since MaxConcurrentFileTransfers doesn't exist
            if (newMaxConcurrency != _currentMaxConcurrency)
            {
                var oldSemaphore = _semaphore;
                _semaphore = new SemaphoreSlim(newMaxConcurrency, newMaxConcurrency);
                _currentMaxConcurrency = newMaxConcurrency;

                // Wait a moment for existing operations to complete
                await Task.Delay(100);
                oldSemaphore?.Dispose();

                _logger.Debug("Updated max concurrent file transfers to: {0}", newMaxConcurrency);
            }
        }
    }
}
