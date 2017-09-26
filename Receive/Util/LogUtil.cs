using System;
using System.IO;

namespace Common
{
    public class Log
    {
        private string logFile;
        private StreamWriter writer;
        private FileStream fileStream = null;

        public Log(string fileName)
        {
            logFile = fileName;
            CreateDirectory(logFile);
        }

        public void log(string info)
        {

            try
            {
                FileInfo fileInfo = new FileInfo(logFile);
                if (!fileInfo.Exists)
                {
                    fileStream = fileInfo.Create();
                    writer = new StreamWriter(fileStream);
                }
                else
                {
                    try
                    {
                        fileStream = fileInfo.Open(FileMode.Append, FileAccess.Write);
                        writer = new StreamWriter(fileStream);
                    }
                    catch (Exception){}                   
                }
                try
                {
                    writer.WriteLine(DateTime.Now + " " + info);
                }
                catch(Exception)
                {

                }

            }
            finally
            {
                if (writer != null)
                {
                    writer.Close();
                    writer.Dispose();
                    fileStream.Close();
                    fileStream.Dispose();
                }
            }
        }

        public void CreateDirectory(string infoPath)
        {
            DirectoryInfo directoryInfo = Directory.GetParent(infoPath);
            if (!directoryInfo.Exists)
            {
                directoryInfo.Create();
            }
        }
    }
}