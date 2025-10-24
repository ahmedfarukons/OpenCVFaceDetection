using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;

namespace OpenCVApp
{
    public partial class Form1 : Form
    {
        VideoCapture capture;
        Mat frame;
        bool isCameraRunning = false;

        // controls are declared in Designer partial class
        // track selected camera index
        private int selectedCameraIndex = 0;

        private CascadeClassifier faceCascade;
        private string cascadePath;
        private const string DefaultCascadeFileName = "haarcascade_frontalface_default.xml";
        private const string DefaultCascadeUrl = "https://raw.githubusercontent.com/opencv/opencv/master/data/haarcascades/haarcascade_frontalface_default.xml";

        public Form1()
        {
            InitializeComponent();

            // Try to auto-load cascade from app directory
            var candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultCascadeFileName);
            if (File.Exists(candidate))
            {
                TryLoadCascade(candidate);
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Enumerate camera indices (try0..5)
            cmbCameras.Items.Clear();
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    using (var vc = new VideoCapture(i))
                    {
                        if (vc.IsOpened())
                        {
                            cmbCameras.Items.Add($"Camera {i}");
                            vc.Release();
                        }
                    }
                }
                catch { }
            }

            if (cmbCameras.Items.Count == 0)
            {
                cmbCameras.Items.Add("Camera0");
            }

            cmbCameras.SelectedIndex = 0;
            selectedCameraIndex = cmbCameras.SelectedIndex;
        }

        private void btnLoadCascade_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Cascade XML (*.xml)|*.xml";
                ofd.Title = "Select Haar Cascade XML file (e.g. haarcascade_frontalface_default.xml)";
                ofd.CheckFileExists = true;
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    var ext = Path.GetExtension(ofd.FileName);
                    if (!string.Equals(ext, ".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        SetInfoText("Please select a .xml cascade file, not an image.");
                        return;
                    }
                    TryLoadCascade(ofd.FileName);
                }
            }
        }

        private void TryLoadCascade(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    SetInfoText("Failed to load cascade: file not found");
                    return;
                }

                if (!IsLikelyValidCascade(path))
                {
                    SetInfoText("Failed to load cascade: file does not look like a valid OpenCV cascade XML. If downloaded from the web, ensure you saved the RAW XML, not the HTML page.");
                    return;
                }

                var cc = new CascadeClassifier(path);
                // small test: ensure it's loaded
                if (!cc.Empty())
                {
                    faceCascade?.Dispose();
                    faceCascade = cc;
                    cascadePath = path;
                    SetInfoText($"Loaded cascade: {Path.GetFileName(path)}");
                    return;
                }
                cc.Dispose();
                SetInfoText("Failed to load cascade: empty classifier");
            }
            catch (Exception ex)
            {
                SetInfoText($"Failed to load cascade: {ex.Message}");
            }
        }

        private bool IsLikelyValidCascade(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length < 1024) return false; // too small
                using (var fs = File.OpenRead(path))
                {
                    byte[] buf = new byte[Math.Min(8192, (int)fi.Length)];
                    int read = fs.Read(buf, 0, buf.Length);
                    string head = System.Text.Encoding.UTF8.GetString(buf, 0, read);
                    head = head.ToLowerInvariant();
                    if (head.Contains("<!doctype html") || head.Contains("<html")) return false; // downloaded webpage
                    bool hasOpencvTags = head.Contains("<opencv_storage>") || head.Contains("opencv_storage") || head.Contains("stages") || head.Contains("cascade");
                    bool hasXmlDecl = head.Contains("<?xml");
                    return hasXmlDecl && hasOpencvTags;
                }
            }
            catch
            {
                return false;
            }
        }

        private void EnsureDefaultCascadeExists(string targetPath)
        {
            try
            {
                if (File.Exists(targetPath) && IsLikelyValidCascade(targetPath)) return;

                // Create directory if needed
                var dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                using (var wc = new WebClient())
                {
                    wc.DownloadFile(DefaultCascadeUrl, targetPath);
                }

                if (!IsLikelyValidCascade(targetPath))
                {
                    try { File.Delete(targetPath); } catch { }
                }
            }
            catch
            {
                // ignore, will fallback to manual selection
            }
        }

        private Bitmap MatToBitmap(Mat mat)
        {
            if (mat == null) return null;
            // Encode to BMP and create a detached Bitmap copy
            // Important: Bitmap(ms) keeps a reference to the stream; to safely Save later,
            // we must return a deep copy that is NOT tied to the stream to avoid GDI+ errors.
            byte[] imgData;
            Cv2.ImEncode(".bmp", mat, out imgData);
            using (var ms = new MemoryStream(imgData))
            using (var temp = new Bitmap(ms))
            {
                return new Bitmap(temp);
            }
        }

        private async void StartCamera()
        {
            if (isCameraRunning)
            {
                SetInfoText("Camera already running");
                return;
            }

            // Toggle buttons for UX
            try
            {
                btnStart.Enabled = false;
                btnStop.Enabled = true;
            }
            catch { }

            // Ensure cascade is available or ask the user (optional)
            if (faceCascade == null || faceCascade.Empty())
            {
                try
                {
                    var defaultXml = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultCascadeFileName);
                    // Try to ensure default cascade exists by downloading if missing
                    EnsureDefaultCascadeExists(defaultXml);
                    if (File.Exists(defaultXml))
                    {
                        TryLoadCascade(defaultXml);
                    }
                    else
                    {
                        using (var ofd = new OpenFileDialog())
                        {
                            ofd.Filter = "Cascade XML (*.xml)|*.xml";
                            ofd.Title = "Select Haar Cascade (optional)";
                            ofd.CheckFileExists = true;
                            if (ofd.ShowDialog() == DialogResult.OK)
                            {
                                var ext = Path.GetExtension(ofd.FileName);
                                if (!string.Equals(ext, ".xml", StringComparison.OrdinalIgnoreCase))
                                {
                                    SetInfoText("Please select a .xml cascade file, not an image.");
                                }
                                else
                                {
                                    TryLoadCascade(ofd.FileName);
                                }
                            }
                            else
                            {
                                SetInfoText("Proceeding without cascade (video only).");
                            }
                        }
                    }
                }
                catch { }
            }

            // Try default backend first using selected camera index
            capture = new VideoCapture(selectedCameraIndex);

            string backendUsed = "Default";

            // If not opened, try DirectShow which often works on Windows
            if (!capture.IsOpened())
            {
                try { capture.Dispose(); } catch { }
                capture = new VideoCapture(selectedCameraIndex, VideoCaptureAPIs.DSHOW);
                backendUsed = "DSHOW";
            }

            if (!capture.IsOpened())
            {
                MessageBox.Show("Unable to open camera. Make sure no other app is using it and permissions are granted.");
                SetInfoText("Camera: not opened");
                return;
            }

            // Read camera properties
            double w = capture.Get(VideoCaptureProperties.FrameWidth);
            double h = capture.Get(VideoCaptureProperties.FrameHeight);
            double fps = capture.Get(VideoCaptureProperties.Fps);
            double fourcc = capture.Get(VideoCaptureProperties.FourCC);

            string fourccStr = "----";
            try
            {
                int fcc = Convert.ToInt32(fourcc);
                char c1 = (char)(fcc & 0xFF);
                char c2 = (char)((fcc >> 8) & 0xFF);
                char c3 = (char)((fcc >> 16) & 0xFF);
                char c4 = (char)((fcc >> 24) & 0xFF);
                fourccStr = string.Concat(c1, c2, c3, c4);
            }
            catch { }

            SetInfoText($"Backend: {backendUsed} Resolution: {w}x{h} FPS: {fps} FOURCC: {fourccStr} Cascade: {(string.IsNullOrEmpty(cascadePath) ? "not loaded" : Path.GetFileName(cascadePath))}");

            frame = new Mat();
            isCameraRunning = true;

            await Task.Run(() =>
            {
                while (isCameraRunning)
                {
                    capture.Read(frame);
                    if (!frame.Empty())
                    {
                        // If cascade loaded, detect faces and draw rectangles
                        if (faceCascade != null && !faceCascade.Empty())
                        {
                            try
                            {
                                using (var gray = new Mat())
                                {
                                    Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                                    Cv2.EqualizeHist(gray, gray);
                                    var faces = faceCascade.DetectMultiScale(gray, 1.1, 3, OpenCvSharp.HaarDetectionTypes.ScaleImage, new OpenCvSharp.Size(30, 30));
                                    foreach (var r in faces)
                                    {
                                        Cv2.Rectangle(frame, r, Scalar.Red, 2);
                                    }

                                    // update info with face count
                                    SetInfoText($"Faces: {faces.Length} Cascade: {(string.IsNullOrEmpty(cascadePath) ? "not loaded" : Path.GetFileName(cascadePath))}");
                                }
                            }
                            catch (Exception ex)
                            {
                                // detection failed, show message
                                SetInfoText("Face detection error: " + ex.Message);
                            }
                        }

                        Bitmap bmp = MatToBitmap(frame);

                        if (pictureBox1.InvokeRequired)
                        {
                            pictureBox1.Invoke(new Action(() =>
                            {
                                var old = pictureBox1.Image;
                                pictureBox1.Image = bmp;
                                old?.Dispose();
                            }));
                        }
                        else
                        {
                            var old = pictureBox1.Image;
                            pictureBox1.Image = bmp;
                            old?.Dispose();
                        }
                    }
                }
            });
        }

        private void SetInfoText(string text)
        {
            if (lblInfo.InvokeRequired)
            {
                lblInfo.Invoke(new Action(() => lblInfo.Text = text));
            }
            else
            {
                lblInfo.Text = text;
            }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            StartCamera();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            isCameraRunning = false;
            try
            {
                capture?.Release();
                capture?.Dispose();
            }
            catch { }

            // clear image
            if (pictureBox1.Image != null)
            {
                var old = pictureBox1.Image;
                pictureBox1.Image = null;
                old?.Dispose();
            }

            SetInfoText("Camera stopped");

            // Toggle buttons back
            try
            {
                btnStart.Enabled = true;
                btnStop.Enabled = false;
            }
            catch { }
        }

        private void btnSnapshot_Click(object sender, EventArgs e)
        {
            try
            {
                if (pictureBox1.Image != null)
                {
                    var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "snapshots");
                    Directory.CreateDirectory(dir);
                    var file = Path.Combine(dir, $"snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    pictureBox1.Image.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                    SetInfoText($"Snapshot saved: {file}");
                }
            }
            catch (Exception ex)
            {
                SetInfoText("Snapshot error: " + ex.Message);
            }
        }

        private void cmbCameras_SelectedIndexChanged(object sender, EventArgs e)
        {
            selectedCameraIndex = cmbCameras.SelectedIndex;
            // if camera running, restart with new index
            if (isCameraRunning)
            {
                btnStop_Click(null, null);
                Task.Delay(200).Wait();
                StartCamera();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            isCameraRunning = false;
            try
            {
                capture?.Release();
                capture?.Dispose();
            }
            catch { }
            faceCascade?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
