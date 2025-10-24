using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
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

        public Form1()
        {
            InitializeComponent();

            // Try to auto-load cascade from app directory
            var candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "haarcascade_frontalface_default.xml");
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
                ofd.Filter = "XML files|*.xml|All files|*.*";
                ofd.Title = "Select Haar Cascade XML file (e.g. haarcascade_frontalface_default.xml)";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    TryLoadCascade(ofd.FileName);
                }
            }
        }

        private void TryLoadCascade(string path)
        {
            try
            {
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

        private Bitmap MatToBitmap(Mat mat)
        {
            if (mat == null) return null;

            // Try to find OpenCvSharp.Extensions.BitmapConverter via reflection
            try
            {
                Type converterType = Type.GetType("OpenCvSharp.Extensions.BitmapConverter, OpenCvSharp.Extensions");
                if (converterType == null)
                {
                    // search loaded assemblies
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            converterType = asm.GetType("OpenCvSharp.Extensions.BitmapConverter");
                            if (converterType != null) break;
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }

                if (converterType != null)
                {
                    var mi = converterType.GetMethod("ToBitmap", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(Mat) }, null);
                    if (mi != null)
                    {
                        var bmp = mi.Invoke(null, new object[] { mat }) as Bitmap;
                        if (bmp != null) return bmp;
                    }
                }
            }
            catch
            {
                // ignore and fallback
            }

            // Fallback: encode to BMP and create Bitmap
            byte[] imgData;
            Cv2.ImEncode(".bmp", mat, out imgData);
            using (var ms = new MemoryStream(imgData))
            {
                return new Bitmap(ms);
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
                    var defaultXml = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "haarcascade_frontalface_default.xml");
                    if (File.Exists(defaultXml))
                    {
                        TryLoadCascade(defaultXml);
                    }
                    else
                    {
                        using (var ofd = new OpenFileDialog())
                        {
                            ofd.Filter = "XML files|*.xml|All files|*.*";
                            ofd.Title = "Select Haar Cascade (optional)";
                            if (ofd.ShowDialog() == DialogResult.OK)
                            {
                                TryLoadCascade(ofd.FileName);
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
