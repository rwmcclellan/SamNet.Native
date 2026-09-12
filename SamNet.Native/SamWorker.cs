// Copyright (c) 2026 Robert W. McClellan, Matthew J. McClellan
// Licensed under the MIT License. See LICENSE in the repository root.

using System;
using System.Collections.Concurrent;

namespace SamNet.Native
{
    public class SamWorker
    {
        private Action<UiMessage>? UiMessageAction;
        private Action<ByteArrayMessageOut>? ByteArrayMessageAction;
        private ConcurrentQueue<object>? MessageToWorker;
        private String WorkerStartupMessage = "No Message";
        bool IsStarted = false;
        private WorkerConfig? WC;
        private Sam3Model? Sam;
        private byte[] ImageData;
        private int ImageWidth = 0;
        private int ImageHeight = 0;    

        public SamWorker(WorkerConfig ipc, ConcurrentQueue<Object>? mtw)
        {
            WC = ipc;
            MessageToWorker = mtw;
            WorkerStartupMessage = $"Worker thread created with DebugLevel: {ipc.DebugLevel}";
            StartWorkerTask();
            ImageData = Array.Empty<byte>();
        }

        private void StartWorkerTask()
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    Sam = new Sam3Model();
                    WorkerCommunication();
                }
                catch (Exception ex)
                {
                    WorkerStartupMessage = "Unexpected Exception in worker thread " + ex.ToString();
                    if (IsStarted)
                    {
                        UiMessage uim = new UiMessage(UiEnum.Exception, WorkerStartupMessage);
                        UiMessageAction?.Invoke(uim);
                    }

                }
            }, TaskCreationOptions.LongRunning);
        }

        public void WorkerCommunication()
        {
            bool keepAlive = true;
            AutoResetEvent AreGovernor = new AutoResetEvent(false);
            IsStarted = true;
            do
            {
                object? ob;
                if ((MessageToWorker is not null) && (MessageToWorker.TryDequeue(out ob)))
                {
                    try
                    {
                        if (ob is MessageToWorker mess)
                        {
                            if (mess.MessE == MessageEnum.Quit)
                            {
                                try
                                {
                                    Sam!.Dispose();
                                    UiMessage uim = new UiMessage(UiEnum.ReadyToQuit, "Exiting");
                                    UiMessageAction?.Invoke(uim);
                                }
                                catch (Exception e)
                                {
                                    UiMessage uim = new UiMessage(UiEnum.Exception, "Error during quit: " + e.Message);
                                    UiMessageAction?.Invoke(uim);
                                }
                                finally
                                {
                                    keepAlive = false;
                                }
                            }
                            else if (mess.MessE == MessageEnum.Task)
                            {
                                if (mess.Command.Equals("LoadModel"))
                                {
                                    if (mess.Ob is string)
                                    {
                                        string path = (string)mess.Ob;
                                        SamResult SamStatus = Sam!.LoadSamCreateState(path);
                                        UiMessage uim = new UiMessage(UiEnum.TaskCompleted, "LoadModel", SamStatus);
                                        UiMessageAction?.Invoke(uim);
                                    }
                                    else
                                    {
                                        UiMessage uim = new UiMessage(UiEnum.TaskCompleted, "LoadModel", "Incorrect data type sent to LoadModel");
                                        UiMessageAction?.Invoke(uim);
                                    }
                                }
                                else if (mess.Command.Equals("LoadImage"))
                                {
                                    if (mess.Ob is ImageInfo)
                                    {
                                        ImageData = ((ImageInfo)mess.Ob).Data;
                                        ImageHeight = ((ImageInfo)mess.Ob).Rows;
                                        ImageWidth = ((ImageInfo)mess.Ob).Cols;
                                        if (ImageData.Length == (ImageHeight*ImageWidth*3))
                                        {
                                            SamResult SamStatus = new SamResult(true, "Image loaded successfully");
                                            UiMessage uim = new UiMessage(UiEnum.TaskCompleted, "LoadImage", SamStatus);
                                            UiMessageAction?.Invoke(uim);
                                        }
                                        else
                                        {
                                            SamResult SamStatus = new SamResult(false, "Incorrect image data size");
                                            UiMessage uim = new UiMessage(UiEnum.TaskCompleted, "LoadImage", SamStatus);
                                            UiMessageAction?.Invoke(uim);
                                        }                                        
                                    }
                                    else
                                    {
                                        SamResult SamStatus = new SamResult(false, "Incorrect data type sent to Encode");
                                        UiMessage uim = new UiMessage(UiEnum.TaskCompleted, "LoadImage", SamStatus);
                                        UiMessageAction?.Invoke(uim);
                                    }
                                }
                                if (mess.Command.Equals("Encode"))
                                {
                                    if (ImageWidth > 0)
                                    {
                                        SamResult SamStatus = Sam!.Encode(ImageData, ImageWidth, ImageHeight, 3);
                                        UiMessage uim = new UiMessage(UiEnum.TaskCompleted, "Encode", SamStatus);
                                        UiMessageAction?.Invoke(uim);
                                    }
                                    else
                                    {
                                        SamResult SamStatus = new SamResult(false, "Incorrect data type sent to Encode");
                                        UiMessage uim = new UiMessage(UiEnum.TaskCompleted, "LoadModel", SamStatus);
                                        UiMessageAction?.Invoke(uim);
                                    }
                                }
                                if (mess.Command.Equals("Sam2Point"))
                                {
                                    SamResult SamStatus = Sam!.ProcessSam2Pvs(mess.Ob);
                                    UiMessage uim = new UiMessage(UiEnum.TaskCompleted, "Sam2Point", SamStatus);
                                    UiMessageAction?.Invoke(uim);
                                }
                                if (mess.Command.Equals("Sam3Prompt"))
                                {
                                    SamResult SamStatus = Sam!.ProcessSam3Pcs(mess.Ob);
                                    UiMessage uim = new UiMessage(UiEnum.TaskCompleted, "Sam3Prompt", SamStatus);
                                    UiMessageAction?.Invoke(uim);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        string mess = "Message to Worker Error " + ex.Message;
                        if (IsStarted)
                        {
                            UiMessage uim = new UiMessage(UiEnum.StatusMessage, mess);
                            UiMessageAction?.Invoke(uim);
                        }
                        else
                        {
                            WorkerStartupMessage = mess;
                        }

                    }

                }
                AreGovernor.WaitOne(100);   // Don't spin
            } while (keepAlive == true);
        }
        public void SetActionDestinations(Action<UiMessage> uim, Action<ByteArrayMessageOut> matm)
        {
            UiMessageAction = uim;
            Sam!.SetUiMessageDestination(UiMessageAction);
            ByteArrayMessageAction = matm;
        }

        public void WorkerStartupStatus()
        {
            UiMessageAction?.Invoke(new UiMessage(UiEnum.StatusMessage, WorkerStartupMessage));
        }

    }

    public class MessageToWorker
    {
        public MessageEnum MessE;
        public string Command;
        public object Ob;

        public MessageToWorker(MessageEnum mse, string s = "", int v = 0)
        {
            MessE = mse;
            Command = s;
            Ob = v;
        }
        public MessageToWorker(MessageEnum mse, string s, string v)
        {
            MessE = mse;
            Command = s;
            Ob = v;
        }
        public MessageToWorker(MessageEnum mse, string s, object v)
        {
            MessE = mse;
            Command = s;
            Ob = v;
        }
    }

    public class WorkerConfig
    {
        public bool IsDebug;
        public int DebugLevel = 0;
    }

    public class ImageInfo
    {
        public byte[] Data;
        public int Rows;
        public int Cols;

        public ImageInfo(byte[] data, int rows, int cols)
        {
            Data = data;
            Rows = rows;
            Cols = cols;
        }
    }

    public class UiMessage
    {
        public UiEnum Code;
        public string Message;
        public string Verbose;
        public object? Ob;

        public UiMessage(UiEnum c, string m, object? ob = null)
        {
            Code = c;
            Message = m;
            Ob = ob;
            Verbose = "";
        }

    }
    public class ByteArrayMessageOut
    {
        public string Message;
        public byte[] Data;
        public int Index;
        public object? Ob;

        public ByteArrayMessageOut(string m, byte[] dt, int i, Object? o = null)
        {
            Data = dt;
            Message = m;
            Index = i;
            Ob = o;
        }
    }
    public enum MessageEnum
    {
        Quit = 1,
        Task = 2
    }
    [Flags]
    public enum SamEnum
    {
        None = 0,
        Points = 1,
        PointPositive = 2,
        PointNegative = 4,
        BoxPositive = 8,
        BoxNegative = 16,
        TextPrompt = 32,
        Sam2Single = 64,
        Sam2MultiMask = 128,
        Sam2Multiple = 256,
        Sam3Result = 512,
        Import = 1024,
        Sam2Manual = 2048
    }

    public enum UiEnum
    {
        Exception = 1,
        TaskCompleted = 2,
        SamDetection = 3,
        ReadyToQuit = 4,
        StatusMessage = 5
    }
}
