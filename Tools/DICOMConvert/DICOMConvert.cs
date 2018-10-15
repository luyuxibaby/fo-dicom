using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Drawing;
using System.Threading;
using Dicom;
using Dicom.Imaging;
using Dicom.Imaging.Render;
using System.Windows.Forms;
using Dicom.IO.Buffer;
using System.IO;
using EvAVIWriter;

namespace DICOMConvert
{
    public class ConvertProcessEventArgs : EventArgs
    {
        public ConvertProcessEventArgs(float nPercent)
        {
            this.Percent = nPercent;
        }
        public float Percent { get; set; }
        public float LastPercent { get; set; }
    }

    public class ExportErrorEventArgs : EventArgs
    {
        public Exception exception { get; set; }
        public ExportErrorEventArgs(Exception e)
        {
            this.exception = e;
        }
    }
    public class Multiframes2SingleframeHelper
    {
        public string SourceFileName;

        public int Percent { get; private set; }

        private Thread m_threadJpeg = null;

        public List<string> JpegFileList = new List<string>();

        private AutoResetEvent terminateConvert;

        private OnProcessEvent callback = null;

        public event EventHandler<ConvertProcessEventArgs> ProcessingJPEG;

        public delegate void OnProcessEvent(float progress);

        public List<string> StartConvertToJpeg(OnProcessEvent callback)
        {
            this.callback = callback;
            m_threadJpeg = new Thread(ExtractMultiframes2Singleframe);
            m_threadJpeg.Start();
            return JpegFileList;

        }

        public void StopConvert()
        {
            this.terminateConvert.Set();
            while (m_threadJpeg != null && m_threadJpeg.IsAlive) { }
            m_threadJpeg = null;
        }

        void ExtractMultiframes2Singleframe(object o)
        {
            float nPercent = 0;
            ConvertProcessEventArgs ev = new ConvertProcessEventArgs(0);
            JpegFileList.Clear();
            if (!File.Exists(SourceFileName)) return;

            DicomFile dcmFile = DicomFile.Open(SourceFileName);
            DicomDataset dcmDataset = dcmFile.Dataset;
            int frames = dcmFile.Dataset.Get<int>(DicomTag.NumberOfFrames);
            if (frames == 0) frames = 1;

            string srcName = Path.GetFileName(SourceFileName);
            string tmpPath = AppDomain.CurrentDomain.BaseDirectory + "tmpframe\\";
            if (!Directory.Exists(tmpPath))
            {
                Directory.CreateDirectory(tmpPath);
            }
            else
            {
                DeleteDir(tmpPath);
            }

            for (int i = 0; i < frames; i++)
            {
                try
                {

                    DicomFile tmpFile = dcmFile.Clone();
                    string sopinstanceuid = tmpFile.Dataset.Get<string>(DicomTag.SOPInstanceUID, "");
                    DicomImage dcmSingleImg = new DicomImage(tmpFile.Dataset);

                    Image img = dcmSingleImg.RenderImage(i);
                    string tmpJpgName = string.Format(sopinstanceuid + "-{0}.jpg", i.ToString().PadLeft(5, '0'), tmpPath);
                    tmpJpgName = tmpPath + tmpJpgName;
                    if (img != null) img.Save(tmpJpgName);
                    JpegFileList.Add(tmpJpgName);
                    nPercent = (i + 1.0f) / frames * 100;
                    string s = nPercent.ToString(("#.##"));
                    callback(nPercent);
                    Thread.Sleep(1);


                }
                catch (Exception e)
                {
                    //MessageBox.Show(string.Format("Errors:{0},Details:{1}", e.Message + e.StackTrace));
                }

                ev.Percent = 100;

            }



        }

        private void OnProcessingJPEG(ConvertProcessEventArgs e)
        {
            if (this.ProcessingJPEG != null)
                this.ProcessingJPEG(this, e);
        }
        public static IByteBuffer ExtractSingleFrame(DicomDataset dataset, int frame)
        {
            DicomPixelData pixelData = DicomPixelData.Create(dataset);
            int frames = pixelData.NumberOfFrames;
            if (frame > frames)
                return null;
            else
            {
                var frameData = pixelData.GetFrame(frame);
                return frameData;

            }
        }
        private static void CreateAndAddPixelData(DicomDataset dataset, IByteBuffer pixelData)
        {
            var syntax = dataset.InternalTransferSyntax;
            if (syntax == DicomTransferSyntax.ImplicitVRLittleEndian)
            {
                var Element = new DicomOtherWord(DicomTag.PixelData, new CompositeByteBuffer());
                CompositeByteBuffer buffer = Element.Buffer as CompositeByteBuffer;
                buffer.Buffers.Add(pixelData);
                dataset.Add(Element);
                return;
            }
            if (syntax.IsEncapsulated)
            {
                var Element = new DicomOtherByteFragment(DicomTag.PixelData);
                long pos = Element.Fragments.Sum(x => (long)x.Size + 8);
                if (pos < uint.MaxValue)
                {
                    Element.OffsetTable.Add((uint)pos);
                }
                else
                {
                    // do not create an offset table for very large datasets
                    Element.OffsetTable.Clear();
                }

                pixelData = EndianByteBuffer.Create(pixelData, dataset.InternalTransferSyntax.Endian, dataset.Get<ushort>(DicomTag.BitsAllocated));
                Element.Fragments.Add(pixelData);
                dataset.Add(Element);
                return;
            }
            if (dataset.Get<ushort>(DicomTag.BitsAllocated) == 16)
            {
                var Element = new DicomOtherWord(DicomTag.PixelData, new CompositeByteBuffer());
                CompositeByteBuffer buffer = Element.Buffer as CompositeByteBuffer;
                buffer.Buffers.Add(pixelData);
                dataset.Add(Element);
            }
            else
            {
                var Element = new DicomOtherByte(DicomTag.PixelData, new CompositeByteBuffer());
                CompositeByteBuffer buffer = Element.Buffer as CompositeByteBuffer;
                buffer.Buffers.Add(pixelData);
                dataset.Add(Element);
            }
        }

        public static bool DeleteDir(string strPath)
        {
            try
            {
                strPath = @strPath.Trim().ToString();
                if (System.IO.Directory.Exists(strPath))
                {
                    string[] strDirs = System.IO.Directory.GetDirectories(strPath);
                    string[] strFiles = System.IO.Directory.GetFiles(strPath);
                    foreach (string strFile in strFiles)
                    {
                        System.Diagnostics.Debug.Write(strFile + "-deleted");
                        System.IO.File.Delete(strFile);
                    }
                    foreach (string strdir in strDirs)
                    {
                        System.Diagnostics.Debug.Write(strdir + "-deleted");
                        System.IO.Directory.Delete(strdir, true);
                    }
                }
                return true;
            }
            catch (Exception Exp)
            {
                System.Diagnostics.Debug.Write(Exp.Message.ToString());
                return false;
            }
        }

    }


    public class DicomFile2AviFileHelper
    {
        private string strMultiFrameDicomFileName = "";
        private int nBeginFrame = 0;
        private int nEndFrame = 0;
        private string strOutPutAVIName = "";
        private int nFrs = 0;

        public List<string> listSingleFrameFileName = new List<string>();
        public int Percent { get; private set; }

        private Thread m_threadAvi = null;

        private AutoResetEvent terminateConvert;

        private OnProcessEvent callback = null;

        public event EventHandler<ConvertProcessEventArgs> ProcessingAVI;

        public delegate void OnProcessEvent(float progress);
        
        public void StartConvertToAVI(OnProcessEvent callback)
        {
            this.callback = callback;
            m_threadAvi = new Thread(ConvertDicom2AVIFormat);
            m_threadAvi.Start();

        }

        public void TestConvertToAVI()
        {

            //

        }

        public void StopConvert()
        {
            this.terminateConvert.Set();
            while (m_threadAvi != null && m_threadAvi.IsAlive) { }
            m_threadAvi = null;
        }

        void ConvertDicom2AVIFormat(object o)
        {
            float nPercent = 0;
            ConvertProcessEventArgs ev = new ConvertProcessEventArgs(0);

            #region 多帧文件存在

            if (File.Exists(strMultiFrameDicomFileName))
            {
                AVIWriter aviWriter = new AVIWriter();

                DicomFile dcmFile = DicomFile.Open(strMultiFrameDicomFileName);


                dcmFile.Dataset.Get<string>(DicomTag.SOPInstanceUID, "");

                int bmWidth = Convert.ToInt16(dcmFile.Dataset.Get<string>(DicomTag.Columns, ""));
                int bmHeight = Convert.ToInt16(dcmFile.Dataset.Get<string>(DicomTag.Rows, ""));

                string strDestFile = strOutPutAVIName;

                aviWriter.Create(strDestFile, (UInt16)nFrs, bmWidth, bmHeight);

                try
                {
                    for (int i = nBeginFrame; i < nEndFrame; i++)
                    {
                        DicomFile tmpFile = dcmFile.Clone();
                        DicomImage dcmSingleImg = new DicomImage(tmpFile.Dataset);
                        Image img = dcmSingleImg.RenderImage(i);
                        Bitmap bmp = new Bitmap(img);
                        bmp.RotateFlip(RotateFlipType.Rotate180FlipX);
                        aviWriter.LoadFrame(bmp);
                        aviWriter.AddFrame();
                        if (nEndFrame == 0)
                            nEndFrame = Convert.ToInt16(dcmFile.Dataset.Get<string>(DicomTag.NumberOfFrames, ""));
                        float f = (float)100 / (float)nEndFrame - nBeginFrame;
                        nPercent += f;
                        string s = nPercent.ToString(("#.##"));
                        ev.LastPercent = ev.Percent;
                        ev.Percent = Convert.ToSingle(s);
                        callback(nPercent);
                        Thread.Sleep(1);
                    }
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.ToString());
                }
                finally
                {
                    aviWriter.Close();
                }
            }
            #endregion

            #region 单帧文件列表存在

            if (listSingleFrameFileName.Count > 0)
            {
                AVIWriter aviWriter = new AVIWriter();

                DicomFile tmp_dcmFile = DicomFile.Open(listSingleFrameFileName[0]);

                int bmWidth = Convert.ToInt16(tmp_dcmFile.Dataset.Get<string>(DicomTag.Columns, ""));
                int bmHeight = Convert.ToInt16(tmp_dcmFile.Dataset.Get<string>(DicomTag.Rows, ""));


                string strDestFile = strOutPutAVIName;

                aviWriter.Create(strDestFile, (UInt16)nFrs, bmWidth, bmHeight);

                try
                {
                    for (int i = 0; i < listSingleFrameFileName.Count; i++)
                    {
                        tmp_dcmFile = DicomFile.Open(listSingleFrameFileName[0]);
                        DicomFile tmpFile = tmp_dcmFile.Clone();
                        DicomImage dcmSingleImg = new DicomImage(tmpFile.Dataset);
                        Image img = dcmSingleImg.RenderImage(0);
                        Bitmap bmp = new Bitmap(img);
                        bmp.RotateFlip(RotateFlipType.Rotate180FlipX);
                        aviWriter.LoadFrame(bmp);
                        aviWriter.AddFrame();
                        float f = (float)100 / (float)listSingleFrameFileName.Count;
                        nPercent += f;
                        string s = nPercent.ToString(("#.##"));
                        ev.LastPercent = ev.Percent;
                        ev.Percent = Convert.ToSingle(s);
                        callback(nPercent);
                        Thread.Sleep(1);
                    }
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.ToString());
                }
                finally
                {
                    aviWriter.Close();
                }

            #endregion



            }

            ev.Percent = 100;

        }

        private void OnProcessingJPEG(ConvertProcessEventArgs e)
        {
            if (this.ProcessingAVI != null)
                this.ProcessingAVI(this, e);
        }
        public static bool DeleteDir(string strPath)
        {
            try
            {
                strPath = @strPath.Trim().ToString();
                if (System.IO.Directory.Exists(strPath))
                {
                    string[] strDirs = System.IO.Directory.GetDirectories(strPath);
                    string[] strFiles = System.IO.Directory.GetFiles(strPath);
                    foreach (string strFile in strFiles)
                    {
                        System.Diagnostics.Debug.Write(strFile + "-deleted");
                        System.IO.File.Delete(strFile);
                    }
                    foreach (string strdir in strDirs)
                    {
                        System.Diagnostics.Debug.Write(strdir + "-deleted");
                        System.IO.Directory.Delete(strdir, true);
                    }
                }
                return true;
            }
            catch (Exception Exp)
            {
                System.Diagnostics.Debug.Write(Exp.Message.ToString());
                return false;
            }
        }

         public void SetAvi(string in_dicomFileName, string out_aviFileName, int nBegin, int nEnd, int Frs)
         {
            strMultiFrameDicomFileName = in_dicomFileName;
            strOutPutAVIName = out_aviFileName;
            nBeginFrame = nBegin;
            nEndFrame = nEnd;
            nFrs = Frs; 
         }

        public void SetAvi(List<string> in_listSingleFrameFileName, string out_aviFileName, int nBegin, int nEnd, int Frs)
         {
             listSingleFrameFileName = in_listSingleFrameFileName;
             strOutPutAVIName = out_aviFileName;
             nBeginFrame = nBegin;
             nEndFrame = nEnd;
             nFrs = Frs; 
         }
    }
}






