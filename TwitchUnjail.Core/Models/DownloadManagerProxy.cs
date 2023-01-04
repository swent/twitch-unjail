using TwitchUnjail.Core.Managers;

namespace TwitchUnjail.Core.Models {
    
    public class DownloadManagerProxy {

        private const int KeyCheckInterval = 100;

        public delegate void DownloaderCommandHandler(object sender, DownloaderCommand command);
        public event DownloaderCommandHandler DownloaderCommand;

        private bool _readKeys;
        private Thread _readingThread;
        private readonly Action<DownloadProgressUpdateEventArgs> _updateCallback;

        public DownloadManagerProxy(Action<DownloadProgressUpdateEventArgs> updateCallback) {
            _readKeys = false;
            _updateCallback = updateCallback;
        }

        public void SignalProgressUpdate(DownloadProgressUpdateEventArgs downloadProgressUpdateEventArgs) {
            _updateCallback(downloadProgressUpdateEventArgs);
        }

        public void StartReadingKeyCommands() {
            if (_readingThread != null) {
                throw new Exception("Already reading key commands.");
            }
            _readKeys = true;
            _readingThread = new Thread(DoReadKeys);
            _readingThread.Start();
        }

        public void StopReadingKeyCommands() {
            _readKeys = false;
            while (_readingThread.IsAlive) {
                Thread.Sleep(KeyCheckInterval);
            }
            _readingThread = null;
        }

        private void DoReadKeys() {
            while (_readKeys) {
                if (Console.KeyAvailable) {
                    var key = Console.ReadKey();
                    switch (key.Key) {
                        case ConsoleKey.P:
                            DownloaderCommand?.Invoke(this, Managers.DownloaderCommand.TogglePause);
                            break;
                        case ConsoleKey.F:
                            DownloaderCommand?.Invoke(this, Managers.DownloaderCommand.ToggleFixedSpeedLimit);
                            break;
                        case ConsoleKey.V:
                            DownloaderCommand?.Invoke(this, Managers.DownloaderCommand.ToggleVariableSpeedLimit);
                            break;
                        case ConsoleKey.OemPlus:
                            DownloaderCommand?.Invoke(this, Managers.DownloaderCommand.IncreaseSpeedLimit);
                            break;
                        case ConsoleKey.OemMinus:
                            DownloaderCommand?.Invoke(this, Managers.DownloaderCommand.DecreaseSpeedLimit);
                            break;
                    }
                }
                Thread.Sleep(KeyCheckInterval);
            }
        }
    }
}
