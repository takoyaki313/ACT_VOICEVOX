using Advanced_Combat_Tracker;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace ACT_Plugin
{
    public class ACT_VOICEVOX : IActPluginV1
    {
        Label lblStatus;
        private FormActMain.PlayTtsDelegate originalTTSDelegate;
        private string configHost = "127.0.0.1";
        private int configPort = 50001; // TCP Server port
        private int configVoicevoxPort = 50021; // VOICEVOX API port
        private string configFilePath = "";
        private int configMode = 0; // 0: TCP Server, 1: SAPI64, 2: VOICEVOX
        private string configOutputDevice = "(Default)";
        private string configAudioOutputDevice = "(Default)";
        private int configPlaybackMode = 0; // 0: Concurrent, 1: Queue (max 4)
        private string configVoicevoxSpeakerName = "四国めたん（ノーマル）";
        private double configVoicevoxSpeed = 1.0;
        private double configVoicevoxVolume = 1.0;
        private bool configRemoveParentheses = false;
        private TextBox txtLog;
        private SpeechSynthesizer synthesizer;
        private Thread playbackWorker;
        private volatile bool playbackWorkerRunning = false;
        private readonly object playbackQueueLock = new object();
        private Queue<string> playbackQueue = new Queue<string>();
        private AutoResetEvent queueEvent = new AutoResetEvent(false);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct WAVEOUTCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public uint dwSupport;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [DllImport("winmm.dll")]
        private static extern int waveOutGetNumDevs();

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int waveOutGetDevCaps(IntPtr uDeviceID, ref WAVEOUTCAPS pwoc, int cbwoc);

        private delegate void WaveOutProc(IntPtr hwo, uint uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID, ref WAVEFORMATEX lpFormat, WaveOutProc dwCallback, IntPtr dwInstance, int dwFlags);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hWaveOut, ref WAVEHDR lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hWaveOut, ref WAVEHDR lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, ref WAVEHDR lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr hWaveOut);

        private void AddLog(string message)
        {
            if (txtLog == null) return;
            Action update = () =>
            {
                List<string> lines = new List<string>(txtLog.Lines);
                lines.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message);
                while (lines.Count > 50) lines.RemoveAt(0);
                txtLog.Lines = lines.ToArray();
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            };

            if (txtLog.InvokeRequired)
            {
                try { txtLog.BeginInvoke((MethodInvoker)(() => update())); }
                catch { }
            }
            else update();
        }

        private void SaveConfig()
        {
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(PluginConfig));
                using (StreamWriter writer = new StreamWriter(configFilePath))
                {
                    PluginConfig config = new PluginConfig 
                    { 
                        Host = this.configHost, 
                        Port = this.configPort, 
                        Mode = this.configMode, 
                        OutputDevice = this.configOutputDevice, 
                        AudioOutputDevice = this.configAudioOutputDevice, 
                        PlaybackMode = this.configPlaybackMode, 
                        VoicevoxSpeakerName = this.configVoicevoxSpeakerName, 
                        VoicevoxSpeed = this.configVoicevoxSpeed, 
                        VoicevoxVolume = this.configVoicevoxVolume,
                        RemoveParentheses = this.configRemoveParentheses
                    };
                    serializer.Serialize(writer, config);
                }
            }
            catch (Exception ex)
            {
                ActGlobals.oFormActMain.WriteExceptionLog(ex, "ACT_VOICEVOX Save Config Error");
            }
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(configFilePath))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(PluginConfig));
                    using (StreamReader reader = new StreamReader(configFilePath))
                    {
                        PluginConfig config = (PluginConfig)serializer.Deserialize(reader);
                        this.configHost = config.Host;
                        this.configPort = config.Port;
                        this.configMode = config.Mode;
                        this.configOutputDevice = config.OutputDevice;
                        this.configAudioOutputDevice = string.IsNullOrEmpty(config.AudioOutputDevice) ? "(Default)" : config.AudioOutputDevice;
                        this.configPlaybackMode = config.PlaybackMode;
                        this.configVoicevoxSpeakerName = config.VoicevoxSpeakerName;
                        this.configVoicevoxSpeed = config.VoicevoxSpeed > 0 ? config.VoicevoxSpeed : 1.0;
                        this.configVoicevoxVolume = config.VoicevoxVolume > 0 ? config.VoicevoxVolume : 1.0;
                        this.configRemoveParentheses = config.RemoveParentheses;
                    }
                }
            }
            catch (Exception ex)
            {
                ActGlobals.oFormActMain.WriteExceptionLog(ex, "ACT_VOICEVOX Load Config Error");
            }
        }

        public void newTTS(string sMessage)
        {
            // 括弧フィルターが有効な場合は括弧内テキストを除去
            if (this.configRemoveParentheses)
            {
                sMessage = RemoveParenthesesContent(sMessage);
            }

            if (this.configMode == 2 && this.configPlaybackMode == 1)
            {
                EnqueuePlayback(sMessage);
                return;
            }

            if (this.configMode == 0) SendViaTCP(sMessage);
            else if (this.configMode == 1) SendViaSAPI(sMessage);
            else if (this.configMode == 2) RunBackground(() => SendViaVOICEVOX(sMessage));
        }

        private string RemoveParenthesesContent(string text)
        {
            // 半角と全角の括弧の組み合わせに対応
            // （内容）、(内容)、（内容)、(内容） のすべてのパターンに対応
            text = Regex.Replace(text, "[（(][^）)]*[）)]", "");
            return text.Trim();
        }

        private void SendViaTCP(string sMessage)
        {
            byte[] bMessage = Encoding.UTF8.GetBytes(sMessage);
            TcpClient tc = null;
            try
            {
                tc = new TcpClient(this.configHost, this.configPort);
            }
            catch (Exception ex)
            {
                ActGlobals.oFormActMain.WriteExceptionLog(ex, "ACT_VOICEVOX Error");
            }

            if (tc != null)
            {
                using (NetworkStream ns = tc.GetStream())
                using (BinaryWriter bw = new BinaryWriter(ns))
                {
                    bw.Write((short)0x0001);
                    bw.Write((short)(-1));
                    bw.Write((short)(-1));
                    bw.Write((short)(-1));
                    bw.Write((short)0);
                    bw.Write((byte)0);
                    bw.Write((int)bMessage.Length);
                    bw.Write(bMessage);
                }
                tc.Close();
            }
        }

        private void SendViaSAPI(string sMessage)
        {
            try
            {
                if (this.synthesizer == null) this.synthesizer = new SpeechSynthesizer();
                
                if (!string.IsNullOrEmpty(this.configOutputDevice) && this.configOutputDevice != "(Default)")
                {
                    try { this.synthesizer.SelectVoice(this.configOutputDevice); }
                    catch { }
                }
                
                this.synthesizer.SpeakAsync(sMessage);
            }
            catch (Exception ex)
            {
                ActGlobals.oFormActMain.WriteExceptionLog(ex, "ACT_VOICEVOX SAPI Error");
            }
        }

        private void SendViaVOICEVOX(string sMessage)
        {
            try
            {
                int speakerId = GetVoicevoxSpeakerIdByName(this.configVoicevoxSpeakerName);
                
                WebClient webClient = new WebClient();
                webClient.Encoding = Encoding.UTF8;
                
                string audioQueryUrl = "http://127.0.0.1:" + this.configVoicevoxPort + "/audio_query?text=" + Uri.EscapeDataString(sMessage) + "&speaker=" + speakerId.ToString();
                byte[] audioQueryData = webClient.UploadData(audioQueryUrl, "POST", new byte[0]);
                string audioQueryResult = Encoding.UTF8.GetString(audioQueryData);
                audioQueryResult = ModifyAudioQueryJson(audioQueryResult, this.configVoicevoxSpeed, this.configVoicevoxVolume);
                
                string synthesisUrl = "http://127.0.0.1:" + this.configVoicevoxPort + "/synthesis?speaker=" + speakerId.ToString();
                webClient.Headers["Content-Type"] = "application/json";
                byte[] audioData = webClient.UploadData(synthesisUrl, "POST", Encoding.UTF8.GetBytes(audioQueryResult));
                
                PlayWavOnDevice(audioData, this.configAudioOutputDevice);
                AddLog("VOICEVOX (" + this.configVoicevoxSpeakerName + "): " + sMessage);
            }
            catch (Exception ex)
            {
                ActGlobals.oFormActMain.WriteExceptionLog(ex, "ACT_VOICEVOX VOICEVOX API Error");
                AddLog("VOICEVOX Error: " + ex.Message);
            }
        }



        private void LoadVoicevoxSpeakers(ComboBox cmbVoicevoxSpeaker)
        {
            try
            {
                WebClient webClient = new WebClient();
                webClient.Encoding = Encoding.UTF8;
                string jsonResult = webClient.DownloadString("http://127.0.0.1:" + this.configVoicevoxPort + "/speakers");
                
                Regex nameRegex = new Regex("\"name\"\\s*:\\s*\"([^\"]+)\"");
                HashSet<string> speakerNames = new HashSet<string>();
                
                cmbVoicevoxSpeaker.Items.Clear();
                foreach (Match match in nameRegex.Matches(jsonResult))
                {
                    string name = match.Groups[1].Value;
                    if (!speakerNames.Contains(name))
                    {
                        speakerNames.Add(name);
                        cmbVoicevoxSpeaker.Items.Add(name);
                    }
                }
                
                int speakerIndex = 0;
                for (int i = 0; i < cmbVoicevoxSpeaker.Items.Count; i++)
                {
                    if (cmbVoicevoxSpeaker.Items[i].ToString() == this.configVoicevoxSpeakerName)
                    {
                        speakerIndex = i;
                        break;
                    }
                }
                cmbVoicevoxSpeaker.SelectedIndex = (speakerIndex >= 0 && speakerIndex < cmbVoicevoxSpeaker.Items.Count) ? speakerIndex : 0;
            }
            catch (Exception ex)
            {
                AddLog("Failed to load VOICEVOX speakers: " + ex.Message);
                cmbVoicevoxSpeaker.Items.Clear();
                cmbVoicevoxSpeaker.Items.Add("四国めたん（ノーマル）");
                cmbVoicevoxSpeaker.SelectedIndex = 0;
            }
        }



        private string ModifyAudioQueryJson(string audioQueryJson, double speed, double volume)
        {
            try
            {
                bool replacedSpeed = false, replacedVolume = false;

                Regex speedRegex = new Regex("\"speedScale\"\\s*:\\s*([0-9.]+)");
                if (speedRegex.IsMatch(audioQueryJson))
                {
                    audioQueryJson = speedRegex.Replace(audioQueryJson, "\"speedScale\":" + speed.ToString("F2"));
                    replacedSpeed = true;
                }

                Regex volumeRegex = new Regex("\"volumeScale\"\\s*:\\s*([0-9.]+)");
                if (volumeRegex.IsMatch(audioQueryJson))
                {
                    audioQueryJson = volumeRegex.Replace(audioQueryJson, "\"volumeScale\":" + volume.ToString("F2"));
                    replacedVolume = true;
                }

                if (!replacedSpeed || !replacedVolume)
                {
                    int braceIndex = audioQueryJson.IndexOf('{');
                    if (braceIndex >= 0)
                    {
                        string injection = "";
                        if (!replacedSpeed) injection += "\"speedScale\":" + speed.ToString("F2");
                        if (!replacedVolume)
                        {
                            if (injection.Length > 0) injection += ",";
                            injection += "\"volumeScale\":" + volume.ToString("F2");
                        }
                        if (injection.Length > 0) audioQueryJson = audioQueryJson.Insert(braceIndex + 1, injection + ",");
                    }
                }
                return audioQueryJson;
            }
            catch (Exception ex)
            {
                AddLog("Error modifying audio query: " + ex.Message);
                return audioQueryJson;
            }
        }

        #region IActPluginV1 Members
        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            lblStatus = pluginStatusText;   // Hand the status label's reference to our local var
            
            // 設定ファイルのパスを設定
            configFilePath = Path.Combine(ActGlobals.oFormActMain.AppDataFolder.FullName, "ACT_VOICEVOX_Config.xml");
            
            // 設定をロード
            LoadConfig();

            Label lblMode = new Label();
            lblMode.Text = "Mode:";
            lblMode.Location = new System.Drawing.Point(10, 10);
            lblMode.AutoSize = true;

            RadioButton rbTCP = new RadioButton();
            rbTCP.Text = "TCP Server";
            rbTCP.Location = new System.Drawing.Point(80, 10);
            rbTCP.Checked = (this.configMode == 0);
            rbTCP.AutoSize = true;

            RadioButton rbSAPI = new RadioButton();
            rbSAPI.Text = "SAPI64";
            rbSAPI.Location = new System.Drawing.Point(180, 10);
            rbSAPI.Checked = (this.configMode == 1);
            rbSAPI.AutoSize = true;

            RadioButton rbVOICEVOX = new RadioButton();
            rbVOICEVOX.Text = "VOICEVOX";
            rbVOICEVOX.Location = new System.Drawing.Point(260, 10);
            rbVOICEVOX.Checked = (this.configMode == 2);
            rbVOICEVOX.AutoSize = true;

            Label lblPlaybackMode = new Label();
            lblPlaybackMode.Text = "Playback:";
            lblPlaybackMode.Location = new System.Drawing.Point(10, 180);
            lblPlaybackMode.AutoSize = true;
            lblPlaybackMode.Visible = (this.configMode == 2);

            ComboBox cmbPlaybackMode = new ComboBox();
            cmbPlaybackMode.Location = new System.Drawing.Point(100, 180);
            cmbPlaybackMode.Width = 250;
            cmbPlaybackMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbPlaybackMode.Items.Add("Concurrent");
            cmbPlaybackMode.Items.Add("Queue (max 4)");
            cmbPlaybackMode.SelectedIndex = (this.configPlaybackMode == 1) ? 1 : 0;
            cmbPlaybackMode.Visible = (this.configMode == 2);

            Label lblHost = new Label();
            lblHost.Text = "Host:";
            lblHost.Location = new System.Drawing.Point(10, 40);
            lblHost.AutoSize = true;
            lblHost.Visible = (this.configMode == 0);

            TextBox txtHost = new TextBox();
            txtHost.Text = this.configHost;
            txtHost.Location = new System.Drawing.Point(80, 40);
            txtHost.Width = 150;
            txtHost.Visible = (this.configMode == 0);

            Label lblPort = new Label();
            lblPort.Text = "Port:";
            lblPort.Location = new System.Drawing.Point(10, 70);
            lblPort.AutoSize = true;
            lblPort.Visible = (this.configMode == 0);

            TextBox txtPort = new TextBox();
            txtPort.Text = this.configPort.ToString();
            txtPort.Location = new System.Drawing.Point(80, 70);
            txtPort.Width = 150;
            txtPort.Visible = (this.configMode == 0);

            Label lblDevice = new Label();
            lblDevice.Text = "Voice:";
            lblDevice.Location = new System.Drawing.Point(10, 40);
            lblDevice.AutoSize = true;
            lblDevice.Visible = (this.configMode == 1);

            ComboBox cmbDevice = new ComboBox();
            cmbDevice.Location = new System.Drawing.Point(80, 40);
            cmbDevice.Width = 250;
            cmbDevice.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbDevice.Visible = (this.configMode == 1);
            
            try
            {
                if (this.synthesizer == null) this.synthesizer = new SpeechSynthesizer();
                cmbDevice.Items.Add("(Default)");
                foreach (var device in this.synthesizer.GetInstalledVoices())
                {
                    cmbDevice.Items.Add(device.VoiceInfo.Name);
                }
                
                // 以前選択したデバイスを復元
                int selectedIndex = 0;
                for (int i = 0; i < cmbDevice.Items.Count; i++)
                {
                    if (cmbDevice.Items[i].ToString() == this.configOutputDevice)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
                cmbDevice.SelectedIndex = (selectedIndex >= 0 && selectedIndex < cmbDevice.Items.Count) ? selectedIndex : 0;
            }
            catch (Exception ex)
            {
                ActGlobals.oFormActMain.WriteExceptionLog(ex, "ACT_VOICEVOX Device List Error");
            }

            Label lblAudioOut = new Label();
            lblAudioOut.Text = "Output Device:";
            lblAudioOut.Location = new System.Drawing.Point(10, 40);
            lblAudioOut.AutoSize = true;
            lblAudioOut.Visible = (this.configMode == 2);

            ComboBox cmbAudioOut = new ComboBox();
            cmbAudioOut.Location = new System.Drawing.Point(130, 40);
            cmbAudioOut.Width = 200;
            cmbAudioOut.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbAudioOut.Visible = (this.configMode == 2);
            cmbAudioOut.Items.Clear();
            cmbAudioOut.Items.Add("(Default)");
            try
            {
                int devs = waveOutGetNumDevs();
                for (int i = 0; i < devs; i++)
                {
                    WAVEOUTCAPS caps = new WAVEOUTCAPS();
                    waveOutGetDevCaps(new IntPtr(i), ref caps, Marshal.SizeOf(typeof(WAVEOUTCAPS)));
                    if (!string.IsNullOrEmpty(caps.szPname))
                    {
                        cmbAudioOut.Items.Add(caps.szPname);
                    }
                }
                int outIndex = 0;
                for (int i = 0; i < cmbAudioOut.Items.Count; i++)
                {
                    if (cmbAudioOut.Items[i].ToString() == this.configAudioOutputDevice)
                    {
                        outIndex = i;
                        break;
                    }
                }
                cmbAudioOut.SelectedIndex = (outIndex >= 0 && outIndex < cmbAudioOut.Items.Count) ? outIndex : 0;
            }
            catch (Exception ex)
            {
                ActGlobals.oFormActMain.WriteExceptionLog(ex, "ACT_VOICEVOX Output Device List Error");
                AddLog("Failed to enumerate output devices: " + ex.Message);
                cmbAudioOut.SelectedIndex = 0;
            }

            Label lblVoicevoxSpeaker = new Label();
            lblVoicevoxSpeaker.Text = "Speaker:";
            lblVoicevoxSpeaker.Location = new System.Drawing.Point(10, 70);
            lblVoicevoxSpeaker.AutoSize = true;
            lblVoicevoxSpeaker.Visible = (this.configMode == 2);

            ComboBox cmbVoicevoxSpeaker = new ComboBox();
            cmbVoicevoxSpeaker.Location = new System.Drawing.Point(130, 70);
            cmbVoicevoxSpeaker.Width = 200;
            cmbVoicevoxSpeaker.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbVoicevoxSpeaker.Visible = (this.configMode == 2);
            
            LoadVoicevoxSpeakers(cmbVoicevoxSpeaker);

            Button btnReloadSpeakers = new Button();
            btnReloadSpeakers.Text = "Reload";
            btnReloadSpeakers.Location = new System.Drawing.Point(340, 70);
            btnReloadSpeakers.Width = 60;
            btnReloadSpeakers.Visible = (this.configMode == 2);
            btnReloadSpeakers.Click += (s, e) =>
            {
                try { LoadVoicevoxSpeakers(cmbVoicevoxSpeaker); }
                catch (Exception ex) { ActGlobals.oFormActMain.WriteExceptionLog(ex, "ACT_VOICEVOX Reload Speakers Error"); }
            };

            // VOICEVOX 速度調整用
            Label lblVoicevoxSpeed = new Label();
            lblVoicevoxSpeed.Text = "Speed:";
            lblVoicevoxSpeed.Location = new System.Drawing.Point(10, 100);
            lblVoicevoxSpeed.AutoSize = true;
            lblVoicevoxSpeed.Visible = (this.configMode == 2);

            TrackBar trkVoicevoxSpeed = new TrackBar();
            trkVoicevoxSpeed.Location = new System.Drawing.Point(80, 100);
            trkVoicevoxSpeed.Width = 200;
            trkVoicevoxSpeed.Height = 25;
            trkVoicevoxSpeed.Minimum = 50;
            trkVoicevoxSpeed.Maximum = 200;
            trkVoicevoxSpeed.Value = (int)(this.configVoicevoxSpeed * 100);
            trkVoicevoxSpeed.Visible = (this.configMode == 2);

            Label lblVoicevoxSpeedValue = new Label();
            lblVoicevoxSpeedValue.Location = new System.Drawing.Point(290, 100);
            lblVoicevoxSpeedValue.Width = 50;
            lblVoicevoxSpeedValue.AutoSize = true;
            lblVoicevoxSpeedValue.Text = this.configVoicevoxSpeed.ToString("F2") + "x";
            lblVoicevoxSpeedValue.Visible = (this.configMode == 2);

            trkVoicevoxSpeed.ValueChanged += (s, e) =>
            {
                this.configVoicevoxSpeed = trkVoicevoxSpeed.Value / 100.0;
                lblVoicevoxSpeedValue.Text = this.configVoicevoxSpeed.ToString("F2") + "x";
                SaveConfig();
            };

            Label lblVoicevoxVolume = new Label();
            lblVoicevoxVolume.Text = "Volume:";
            lblVoicevoxVolume.Location = new System.Drawing.Point(10, 145);
            lblVoicevoxVolume.AutoSize = true;
            lblVoicevoxVolume.Visible = (this.configMode == 2);

            TrackBar trkVoicevoxVolume = new TrackBar();
            trkVoicevoxVolume.Location = new System.Drawing.Point(80, 145);
            trkVoicevoxVolume.Width = 200;
            trkVoicevoxVolume.Height = 40;
            trkVoicevoxVolume.Minimum = 0;
            trkVoicevoxVolume.Maximum = 200;
            trkVoicevoxVolume.Value = (int)(this.configVoicevoxVolume * 100);
            trkVoicevoxVolume.TickStyle = TickStyle.None;
            trkVoicevoxVolume.Visible = (this.configMode == 2);

            Label lblVoicevoxVolumeValue = new Label();
            lblVoicevoxVolumeValue.Location = new System.Drawing.Point(290, 160);
            lblVoicevoxVolumeValue.Width = 50;
            lblVoicevoxVolumeValue.AutoSize = true;
            lblVoicevoxVolumeValue.Text = (this.configVoicevoxVolume * 100).ToString("F0") + "%";
            lblVoicevoxVolumeValue.Visible = (this.configMode == 2);

            trkVoicevoxVolume.ValueChanged += (s, e) =>
            {
                this.configVoicevoxVolume = trkVoicevoxVolume.Value / 100.0;
                lblVoicevoxVolumeValue.Text = (this.configVoicevoxVolume * 100).ToString("F0") + "%";
                SaveConfig();
            };

            Label lblRemoveParentheses = new Label();
            lblRemoveParentheses.Text = "Remove Parentheses:";
            lblRemoveParentheses.Location = new System.Drawing.Point(10, 220);
            lblRemoveParentheses.AutoSize = true;

            CheckBox chkRemoveParentheses = new CheckBox();
            chkRemoveParentheses.Text = "ON";
            chkRemoveParentheses.Location = new System.Drawing.Point(200, 220);
            chkRemoveParentheses.Checked = this.configRemoveParentheses;
            chkRemoveParentheses.AutoSize = true;
            chkRemoveParentheses.CheckedChanged += (s, e) =>
            {
                this.configRemoveParentheses = chkRemoveParentheses.Checked;
                SaveConfig();
            };

            Button btnTest = new Button();
            btnTest.Text = "Test";
            btnTest.Location = new System.Drawing.Point(10, 265);
            btnTest.Click += (s, e) =>
            {
                try
                {
                    if (rbTCP.Checked)
                    {
                        SendViaTCP("テスト音声です");
                    }
                    else if (rbVOICEVOX.Checked)
                    {
                        if (cmbVoicevoxSpeaker.SelectedItem != null)
                            this.configVoicevoxSpeakerName = cmbVoicevoxSpeaker.SelectedItem.ToString();
                        if (this.configPlaybackMode == 1)
                            EnqueuePlayback("テスト音声です");
                        else
                            RunBackground(() => SendViaVOICEVOX("テスト音声です"));
                    }
                    else
                    {
                        if (this.synthesizer == null) this.synthesizer = new SpeechSynthesizer();
                        if (cmbDevice.SelectedItem != null && cmbDevice.SelectedItem.ToString() != "(Default)")
                        {
                            try { this.synthesizer.SelectVoice(cmbDevice.SelectedItem.ToString()); }
                            catch (Exception ex) { ActGlobals.oFormActMain.WriteExceptionLog(ex, "ACT_VOICEVOX SelectVoice Error"); return; }
                        }
                        this.synthesizer.SpeakAsync("テスト音声です");
                    }
                }
                catch (Exception ex)
                {
                    ActGlobals.oFormActMain.WriteExceptionLog(ex, "ACT_VOICEVOX Test Play Error");
                }
            };

            EventHandler modeChangeHandler = (s, e) =>
            {
                int newMode = rbTCP.Checked ? 0 : (rbVOICEVOX.Checked ? 2 : 1);
                this.configMode = newMode;
                SaveConfig();
                
                lblHost.Visible = (newMode == 0);
                txtHost.Visible = (newMode == 0);
                lblPort.Visible = (newMode == 0);
                txtPort.Visible = (newMode == 0);
                
                lblDevice.Visible = (newMode == 1);
                cmbDevice.Visible = (newMode == 1);
                
                lblAudioOut.Visible = (newMode == 2);
                cmbAudioOut.Visible = (newMode == 2);
                lblVoicevoxSpeaker.Visible = (newMode == 2);
                cmbVoicevoxSpeaker.Visible = (newMode == 2);
                btnReloadSpeakers.Visible = (newMode == 2);
                lblVoicevoxSpeed.Visible = (newMode == 2);
                trkVoicevoxSpeed.Visible = (newMode == 2);
                lblVoicevoxSpeedValue.Visible = (newMode == 2);
                lblVoicevoxVolume.Visible = (newMode == 2);
                trkVoicevoxVolume.Visible = (newMode == 2);
                lblVoicevoxVolumeValue.Visible = (newMode == 2);
                lblPlaybackMode.Visible = (newMode == 2);
                cmbPlaybackMode.Visible = (newMode == 2);
            };
            rbTCP.CheckedChanged += modeChangeHandler;
            rbSAPI.CheckedChanged += modeChangeHandler;
            rbVOICEVOX.CheckedChanged += modeChangeHandler;

            cmbPlaybackMode.SelectedIndexChanged += (s, e) =>
            {
                this.configPlaybackMode = cmbPlaybackMode.SelectedIndex == 1 ? 1 : 0;
                SaveConfig();
                if (this.configMode == 2 && this.configPlaybackMode == 1) EnsurePlaybackWorker();
            };

            EventHandler autoSaveHandler = (s, e) =>
            {
                this.configHost = txtHost.Text;
                int port;
                if (int.TryParse(txtPort.Text, out port))
                {
                    this.configPort = port;
                }
                
                if (cmbDevice.SelectedItem != null)
                {
                    this.configOutputDevice = cmbDevice.SelectedItem.ToString();
                }
                
                if (cmbAudioOut.SelectedItem != null)
                {
                    this.configAudioOutputDevice = cmbAudioOut.SelectedItem.ToString();
                }
                
                if (cmbVoicevoxSpeaker.SelectedItem != null)
                {
                    this.configVoicevoxSpeakerName = cmbVoicevoxSpeaker.SelectedItem.ToString();
                }
                
                SaveConfig();
            };
            txtHost.TextChanged += autoSaveHandler;
            txtPort.TextChanged += autoSaveHandler;
            cmbDevice.SelectedIndexChanged += autoSaveHandler;
            cmbAudioOut.SelectedIndexChanged += autoSaveHandler;
            cmbVoicevoxSpeaker.SelectedIndexChanged += autoSaveHandler;

            Label lblLog = new Label();
            lblLog.Text = "Log:";
            lblLog.Location = new System.Drawing.Point(10, 295);
            lblLog.AutoSize = true;

            txtLog = new TextBox();
            txtLog.Location = new System.Drawing.Point(10, 315);
            txtLog.Width = 580;
            txtLog.Height = 150;
            txtLog.Multiline = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.ReadOnly = true;

            pluginScreenSpace.Controls.Add(lblMode);
            pluginScreenSpace.Controls.Add(rbTCP);
            pluginScreenSpace.Controls.Add(rbSAPI);
            pluginScreenSpace.Controls.Add(rbVOICEVOX);
            pluginScreenSpace.Controls.Add(lblPlaybackMode);
            pluginScreenSpace.Controls.Add(cmbPlaybackMode);
            pluginScreenSpace.Controls.Add(lblHost);
            pluginScreenSpace.Controls.Add(txtHost);
            pluginScreenSpace.Controls.Add(lblPort);
            pluginScreenSpace.Controls.Add(txtPort);
            pluginScreenSpace.Controls.Add(lblDevice);
            pluginScreenSpace.Controls.Add(cmbDevice);
            pluginScreenSpace.Controls.Add(lblVoicevoxSpeaker);
            pluginScreenSpace.Controls.Add(cmbVoicevoxSpeaker);
            pluginScreenSpace.Controls.Add(btnReloadSpeakers);
            pluginScreenSpace.Controls.Add(lblVoicevoxSpeed);
            pluginScreenSpace.Controls.Add(trkVoicevoxSpeed);
            pluginScreenSpace.Controls.Add(lblVoicevoxSpeedValue);
            pluginScreenSpace.Controls.Add(lblVoicevoxVolume);
            pluginScreenSpace.Controls.Add(trkVoicevoxVolume);
            pluginScreenSpace.Controls.Add(lblVoicevoxVolumeValue);
            pluginScreenSpace.Controls.Add(lblAudioOut);
            pluginScreenSpace.Controls.Add(cmbAudioOut);
            pluginScreenSpace.Controls.Add(lblRemoveParentheses);
            pluginScreenSpace.Controls.Add(chkRemoveParentheses);
            pluginScreenSpace.Controls.Add(btnTest);
            pluginScreenSpace.Controls.Add(lblLog);
            pluginScreenSpace.Controls.Add(txtLog);

            this.originalTTSDelegate = (FormActMain.PlayTtsDelegate)ActGlobals.oFormActMain.PlayTtsMethod.Clone();
            ActGlobals.oFormActMain.PlayTtsMethod = new FormActMain.PlayTtsDelegate(this.newTTS);

            lblStatus.Text = "Plugin Started";
        }

        private int GetVoicevoxSpeakerIdByName(string speakerName)
        {
            try
            {
                WebClient webClient = new WebClient();
                webClient.Encoding = Encoding.UTF8;
                string jsonResult = webClient.DownloadString("http://127.0.0.1:" + this.configVoicevoxPort + "/speakers");
                
                Regex speakerBlockRegex = new Regex("\"name\"\\s*:\\s*\"" + Regex.Escape(speakerName) + "\".*?\"id\"\\s*:\\s*(\\d+)");
                Match match = speakerBlockRegex.Match(jsonResult);
                
                if (match.Success) return int.Parse(match.Groups[1].Value);
                
                Regex nameIdRegex = new Regex("\"name\"\\s*:\\s*\"" + Regex.Escape(speakerName) + "\"");
                Match nameMatch = nameIdRegex.Match(jsonResult);
                
                if (nameMatch.Success)
                {
                    int searchStartIndex = nameMatch.Index + nameMatch.Length;
                    Regex idAfterNameRegex = new Regex("\"id\"\\s*:\\s*(\\d+)");
                    Match idMatch = idAfterNameRegex.Match(jsonResult, searchStartIndex);
                    if (idMatch.Success) return int.Parse(idMatch.Groups[1].Value);
                }
                return 0;
            }
            catch (Exception ex)
            {
                AddLog("Error getting speaker ID: " + ex.Message);
                return 0;
            }
        }

        public void DeInitPlugin()
        {
            try
            {
                playbackWorkerRunning = false;
                if (queueEvent != null) queueEvent.Set();
                if (playbackWorker != null && playbackWorker.IsAlive) playbackWorker.Join(1000);
            }
            catch { }
            if (this.originalTTSDelegate != null) ActGlobals.oFormActMain.PlayTtsMethod = this.originalTTSDelegate;
            lblStatus.Text = "Plugin Exited";
        }
        #endregion
     
        private void EnsurePlaybackWorker()
        {
            if (playbackWorkerRunning) return;
            playbackWorkerRunning = true;
            playbackWorker = new Thread(() =>
            {
                try
                {
                    while (playbackWorkerRunning)
                    {
                        queueEvent.WaitOne();
                        while (playbackWorkerRunning)
                        {
                            string msg = null;
                            lock (playbackQueueLock)
                            {
                                if (playbackQueue.Count > 0) msg = playbackQueue.Dequeue();
                            }
                            if (msg == null) break;
                            try
                            {
                                if (this.configMode == 0) SendViaTCP(msg);
                                else if (this.configMode == 1) PlayViaSAPIBlocking(msg);
                                else SendViaVOICEVOX(msg);
                            }
                            catch (Exception ex)
                            {
                                ActGlobals.oFormActMain.WriteExceptionLog(ex, "ACT_VOICEVOX Queue Worker Error");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ActGlobals.oFormActMain.WriteExceptionLog(ex, "ACT_VOICEVOX Queue Worker Fatal");
                }
            });
            playbackWorker.IsBackground = true;
            playbackWorker.Start();
        }

        private void EnqueuePlayback(string sMessage)
        {
            EnsurePlaybackWorker();
            lock (playbackQueueLock)
            {
                if (playbackQueue.Count < 4)
                {
                    playbackQueue.Enqueue(sMessage);
                    queueEvent.Set();
                }
            }
        }

        private void PlayViaSAPIBlocking(string sMessage)
        {
            try
            {
                if (this.synthesizer == null) this.synthesizer = new SpeechSynthesizer();
                if (!string.IsNullOrEmpty(this.configOutputDevice) && this.configOutputDevice != "(Default)")
                {
                    try { this.synthesizer.SelectVoice(this.configOutputDevice); } catch { }
                }
                this.synthesizer.Speak(sMessage);
            }
            catch (Exception ex)
            {
                ActGlobals.oFormActMain.WriteExceptionLog(ex, "ACT_VOICEVOX SAPI Blocking Error");
            }
        }
        private void RunBackground(Action action)
        {
            try
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { action(); }
                    catch (Exception ex) { ActGlobals.oFormActMain.WriteExceptionLog(ex, "ACT_VOICEVOX Background Error"); }
                });
            }
            catch (Exception ex)
            {
                ActGlobals.oFormActMain.WriteExceptionLog(ex, "ACT_VOICEVOX Queue Error");
            }
        }

        private void ParseWav(byte[] wavBytes, out WAVEFORMATEX format, out byte[] data)
        {
            format = new WAVEFORMATEX();
            data = new byte[0];

            using (var ms = new MemoryStream(wavBytes))
            using (var br = new BinaryReader(ms))
            {
                if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidOperationException("Invalid WAV");
                br.ReadInt32();
                if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidOperationException("Invalid WAV");

                bool fmtFound = false, dataFound = false;

                while (ms.Position + 8 <= ms.Length)
                {
                    string chunkId = new string(br.ReadChars(4));
                    int chunkSize = br.ReadInt32();

                    if (chunkId == "fmt ")
                    {
                        format.wFormatTag = br.ReadUInt16();
                        format.nChannels = br.ReadUInt16();
                        format.nSamplesPerSec = br.ReadUInt32();
                        format.nAvgBytesPerSec = br.ReadUInt32();
                        format.nBlockAlign = br.ReadUInt16();
                        format.wBitsPerSample = br.ReadUInt16();
                        format.cbSize = (ushort)((chunkSize > 16) ? br.ReadUInt16() : 0);
                        if (chunkSize > 16) ms.Position += (chunkSize - 18);
                        fmtFound = true;
                    }
                    else if (chunkId == "data")
                    {
                        data = br.ReadBytes(chunkSize);
                        dataFound = true;
                    }
                    else ms.Position += chunkSize;

                    if (fmtFound && dataFound) break;
                }

                if (!fmtFound || !dataFound) throw new InvalidOperationException("Invalid WAV");
            }
        }

        private void PlayWavOnDevice(byte[] wavBytes, string deviceName)
        {
            try
            {
                WAVEFORMATEX fmt;
                byte[] pcmData;
                ParseWav(wavBytes, out fmt, out pcmData);
                int deviceId = -1;

                if (!string.IsNullOrEmpty(deviceName) && deviceName != "(Default)")
                {
                    int devs = waveOutGetNumDevs();
                    for (int i = 0; i < devs; i++)
                    {
                        WAVEOUTCAPS caps = new WAVEOUTCAPS();
                        waveOutGetDevCaps(new IntPtr(i), ref caps, Marshal.SizeOf(typeof(WAVEOUTCAPS)));
                        if (caps.szPname == deviceName)
                        {
                            deviceId = i;
                            break;
                        }
                    }
                }

                IntPtr hWaveOut;
                if (waveOutOpen(out hWaveOut, deviceId, ref fmt, null, IntPtr.Zero, 0) != 0) return;

                GCHandle handle = GCHandle.Alloc(pcmData, GCHandleType.Pinned);
                try
                {
                    WAVEHDR hdr = new WAVEHDR
                    {
                        lpData = handle.AddrOfPinnedObject(),
                        dwBufferLength = (uint)pcmData.Length,
                        dwBytesRecorded = 0,
                        dwUser = IntPtr.Zero,
                        dwFlags = 0,
                        dwLoops = 0,
                        lpNext = IntPtr.Zero,
                        reserved = IntPtr.Zero
                    };

                    if (waveOutPrepareHeader(hWaveOut, ref hdr, Marshal.SizeOf(typeof(WAVEHDR))) != 0)
                    {
                        waveOutClose(hWaveOut);
                        return;
                    }

                    if (waveOutWrite(hWaveOut, ref hdr, Marshal.SizeOf(typeof(WAVEHDR))) != 0)
                    {
                        waveOutUnprepareHeader(hWaveOut, ref hdr, Marshal.SizeOf(typeof(WAVEHDR)));
                        waveOutClose(hWaveOut);
                        return;
                    }

                    while ((hdr.dwFlags & 0x00000001) == 0) Thread.Sleep(10);
                    waveOutUnprepareHeader(hWaveOut, ref hdr, Marshal.SizeOf(typeof(WAVEHDR)));
                }
                finally
                {
                    handle.Free();
                    waveOutClose(hWaveOut);
                }
            }
            catch { }
        }
     }

    [XmlRoot("PluginConfig")]
    public class PluginConfig
    {
        [XmlElement("Host")]
        public string Host { get; set; }

        [XmlElement("Port")]
        public int Port { get; set; }

        [XmlElement("Mode")]
        public int Mode { get; set; }

        [XmlElement("OutputDevice")]
        public string OutputDevice { get; set; }

        [XmlElement("AudioOutputDevice")]
        public string AudioOutputDevice { get; set; }

        [XmlElement("PlaybackMode")]
        public int PlaybackMode { get; set; }

        [XmlElement("VoicevoxSpeakerName")]
        public string VoicevoxSpeakerName { get; set; }

        [XmlElement("VoicevoxSpeed")]
        public double VoicevoxSpeed { get; set; }

        [XmlElement("VoicevoxVolume")]
        public double VoicevoxVolume { get; set; }

        [XmlElement("RemoveParentheses")]
        public bool RemoveParentheses { get; set; }
    }
}
