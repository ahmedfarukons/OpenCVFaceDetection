# OpenCV Face Detector (WinForms + OpenCvSharp)

A minimal Windows Forms app that captures webcam video and performs face detection with OpenCV Haar cascades.

## Features
- Start/Stop webcam stream
- Optional auto-download and load of haarcascade_frontalface_default.xml
- Manual cascade selection with .xml filter
- Snapshot saving to snapshots/

## Usage
1. Build and run the solution.
2. Click Start to begin streaming. The app will try to auto-download the cascade and load it.
3. Alternatively, click Load Cascade to pick a .xml cascade file.
4. Click Snapshot to save a PNG under snapshots/.

## Notes
- If the cascade fails to load, ensure you are using the RAW XML (not an HTML page).
- GDI+ snapshot errors are avoided by detaching the Bitmap from the stream.
- Works with OpenCvSharp 4.11 runtime on .NET Framework 4.7.2.
