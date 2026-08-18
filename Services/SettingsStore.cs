using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Threading;
using System.Xml;
using MouseCursorTrap.Models;

namespace MouseCursorTrap.Services
{
    /// <summary>
    /// 설정을 %AppData%\MouseCursorTrap\settings.json 에 읽고 쓴다.
    /// 이 창에는 확인/취소 버튼이 없으므로 변경 즉시 저장하되, 300ms 디바운스로 쓰기 폭주를 뭉친다.
    /// </summary>
    internal sealed class SettingsStore : IDisposable
    {
        private const string FolderName = "MouseCursorTrap";
        private const string FileName = "settings.json";

        private readonly string _path;
        private readonly DispatcherTimer _saveTimer;

        private AppSettings _settings;
        private bool _dirty;
        private bool _recovered;

        public SettingsStore()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);
            _path = Path.Combine(folder, FileName);

            _saveTimer = new DispatcherTimer(DispatcherPriority.Background);
            _saveTimer.Interval = TimeSpan.FromMilliseconds(300);
            _saveTimer.Tick += OnSaveTimerTick;
        }

        public string FilePath { get { return _path; } }

        /// <summary>파일을 읽는다. 없거나 손상되었으면 기본값을 돌려준다.</summary>
        public AppSettings Load()
        {
            bool existed = File.Exists(_path);
            _recovered = false;

            _settings = LoadCore();
            _settings.Normalize();

            if (!existed || _recovered)
            {
                // 첫 실행에서도 파일이 생기도록, 손상된 파일은 정상 내용으로 되돌리도록
                // 기본값을 한 번 기록한다. 사용자가 열어 보거나 손으로 고칠 수 있어야 한다.
                MarkDirty();
            }

            return _settings;
        }

        private AppSettings LoadCore()
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            try
            {
                byte[] content = File.ReadAllBytes(_path);

                // 사람이 손으로 고친 파일에는 BOM이 붙어 있을 수 있다.
                // DataContractJsonSerializer는 BOM을 만나면 예외를 내므로 미리 걷어낸다.
                int offset = HasUtf8Bom(content) ? 3 : 0;

                using (MemoryStream stream = new MemoryStream(content, offset, content.Length - offset, false))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    AppSettings loaded = serializer.ReadObject(stream) as AppSettings;
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
            }
            catch (SerializationException) { }
            catch (XmlException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            // 손상된 파일은 진단용으로 남겨 두고 기본값으로 계속 진행한다.
            _recovered = true;
            TryBackupCorruptFile();
            return new AppSettings();
        }

        private static bool HasUtf8Bom(byte[] content)
        {
            return content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF;
        }

        private void TryBackupCorruptFile()
        {
            try
            {
                File.Copy(_path, _path + ".bak", true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>변경이 생겼음을 알린다. 실제 저장은 디바운스 후에 일어난다.</summary>
        public void MarkDirty()
        {
            _dirty = true;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void OnSaveTimerTick(object sender, EventArgs e)
        {
            _saveTimer.Stop();
            SaveNow();
        }

        /// <summary>대기 중인 저장을 즉시 반영한다. 종료 경로에서 호출한다.</summary>
        public void FlushPendingSave()
        {
            _saveTimer.Stop();
            if (_dirty)
            {
                SaveNow();
            }
        }

        private void SaveNow()
        {
            if (_settings == null)
            {
                return;
            }

            try
            {
                string folder = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                string temporaryPath = _path + ".tmp";

                using (FileStream stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    // WriteObject(Stream, ...)는 한 줄로 압축된 JSON을 낸다.
                    // 사람이 열어 볼 파일이므로 들여쓰기 writer를 거친다.
                    using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(
                        stream, new UTF8Encoding(false), false, true, "  "))
                    {
                        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                        serializer.WriteObject(writer, _settings);
                        writer.Flush();
                    }

                    stream.Flush(true);
                }

                ReplaceFile(temporaryPath, _path);
                _dirty = false;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SerializationException) { }
        }

        private static void ReplaceFile(string temporaryPath, string targetPath)
        {
            if (!File.Exists(targetPath))
            {
                File.Move(temporaryPath, targetPath);
                return;
            }

            try
            {
                File.Replace(temporaryPath, targetPath, null, true);
            }
            catch (IOException)
            {
                // 비NTFS 볼륨이나 백신 잠금 등으로 File.Replace가 실패할 수 있다.
                File.Copy(temporaryPath, targetPath, true);
                TryDelete(temporaryPath);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(temporaryPath, targetPath, true);
                TryDelete(temporaryPath);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Dispose()
        {
            _saveTimer.Tick -= OnSaveTimerTick;
            _saveTimer.Stop();
        }
    }
}
