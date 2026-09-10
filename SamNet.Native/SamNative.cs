// Copyright (c) 2026 Robert W. McClellan, Matthew J. McClellan
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Drawing;
using System.Runtime.InteropServices;
using static SamNet.Native.SamNative;

namespace SamNet.Native
{
    public static class SamNative
    {
        private const string DllName = "sam3";

        // Opaque handles 
        public static IntPtr model;
        public static IntPtr state;

        [StructLayout(LayoutKind.Sequential)]
        public struct Sam3Point
        {
            public float X;
            public float Y;
            public Sam3Point(float x, float y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Sam3Box
        {
            public float X0, Y0, X1, Y1;
            public Sam3Box(float x0, float y0, float x1, float y1)
            {
                X0 = x0; Y0 = y0; X1 = x1; Y1 = y1;
            }
        }


        // --- P/Invoke Signatures --- 

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr sam3_load_model_c([MarshalAs(UnmanagedType.LPStr)] string model_path);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr sam3_create_state_c(IntPtr model);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void sam3_free_model_c(IntPtr model);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void sam3_free_state_c(IntPtr state);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool sam3_encode_image_from_buffer_c(
       IntPtr state,
       IntPtr model,
       IntPtr pixels,          // pointer to tightly-packed RGB/RGBA
       int width,
       int height,
       int channels);

        [DllImport("sam3", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr sam3_segment_pvs_c(IntPtr state, IntPtr model, [In] Sam3Point[] posPoints, int numPosPoints,
            [In] Sam3Point[] negPoints, int numNegPoints, IntPtr box, [MarshalAs(UnmanagedType.I1)] bool multimask);

        // Overload-friendly helpers are useful; for simplicity many people pass
        // a dummy box + a flag, or use two entry points (with/without box).

        [DllImport("sam3", CallingConvention = CallingConvention.Cdecl)]
        public static extern void sam3_free_result_c(IntPtr result);

        [DllImport("sam3", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sam3_result_num_detections(IntPtr result);

        [DllImport("sam3", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr sam3_result_mask(
            IntPtr result, int index, out int width, out int height);

        [DllImport("sam3", CallingConvention = CallingConvention.Cdecl)]
        public static extern float sam3_result_score(IntPtr result, int index);

        [DllImport("sam3", CallingConvention = CallingConvention.Cdecl)]
        public static extern float sam3_result_iou(IntPtr result, int index);

        [DllImport("sam3", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr sam3_segment_pcs_c(IntPtr state, IntPtr model,
            [MarshalAs(UnmanagedType.LPStr)] string textPrompt, float scoreThreshold, float nmsThreshold);

        [DllImport("sam3", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr sam3_segment_pcs_ext_c(IntPtr state, IntPtr model,
            [MarshalAs(UnmanagedType.LPStr)] string textPrompt, [In] Sam3Box[] posBoxes, int numPosBoxes,
        [In] Sam3Box[] negBoxes, int numNegBoxes, float scoreThreshold, float nmsThreshold);

        [DllImport("sam3", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool sam3_result_box(IntPtr result, int index, out float x0, out float y0, out float x1, out float y1);

    }
    public sealed class Sam3Model : IDisposable
    {
        private IntPtr _model = IntPtr.Zero;
        private IntPtr _state = IntPtr.Zero;

        private Action<UiMessage>? UiMessageAction;

        public void SetUiMessageDestination(Action<UiMessage> md)
        {
            UiMessageAction = md;
        }

        public SamResult LoadSamCreateState(string modelPath)
        {
            try
            {
                _model = SamNative.sam3_load_model_c(modelPath);
                if (_model == IntPtr.Zero) return new SamResult(false, "Failed to load model");
            }
            catch (Exception ex)
            {
                return new SamResult(false, "Exception at Load", ex.Message);
            }

            try
            {
                _state = SamNative.sam3_create_state_c(_model);
                if (_state == IntPtr.Zero) return new SamResult(false, "Failed to create state");
            }
            catch (Exception ex)
            {
                return new SamResult(false, "Exception at CreateState", ex.Message);
            }

            return new SamResult();
        }

        /// <summary>
        /// Encode an image that is already in memory as tightly-packed RGB or RGBA.
        /// </summary>
        public SamResult Encode(byte[] pixels, int width, int height, int channels)
        {
            if (pixels == null) return new SamResult(false, "Pixels null at encode step");
            if (channels != 3 && channels != 4) return new SamResult(false, "Only 3 (RGB) or 4 (RGBA) channels are supported");

            // Pin the managed array so the GC cannot move it during the native call
            GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();
                bool IsGood = SamNative.sam3_encode_image_from_buffer_c(_state, _model, ptr, width, height, channels);
                if (IsGood)
                {
                    return new SamResult();
                }
                else
                {
                    return new SamResult(false, "Encode failed at encode_image_from_buffer");
                }

            }
            catch (Exception ex)
            {
                return new SamResult(false, "Encode exception at encode_image_from_buffer", ex.Message);
            }
            finally
            {
                handle.Free();
            }
        }


        public SamResult ProcessSam2Pvs(object Ob)
        {
            SamMessage sm = (SamMessage)Ob;
            bool isMultiMask = sm.Senum.HasFlag(SamEnum.Sam2MultiMask);
            List<Sam3Point> positiveList = new List<Sam3Point>();
            List<Sam3Point> negativeList = new List<Sam3Point>();
            List<System.Drawing.Rectangle> boxList = new List<System.Drawing.Rectangle>();

            for (int i = 0; i < sm.Info.Count(); i++)
            {
                if (sm.Info[i].EntryEnum.HasFlag(SamEnum.PointPositive)) positiveList.Add(new Sam3Point(sm.Info[i].X, sm.Info[i].Y));
                if (sm.Info[i].EntryEnum.HasFlag(SamEnum.PointNegative)) negativeList.Add(new Sam3Point(sm.Info[i].X, sm.Info[i].Y));
                if (sm.Info[i].EntryEnum.HasFlag(SamEnum.BoxPositive)) boxList.Add(sm.Info[i].Box);
            }
            Sam3Point[] posPoints = positiveList.ToArray();
            Sam3Point[] negPoints = negativeList.ToArray();

            int numPos = posPoints.Length;
            int numNeg = negPoints.Length;

            // Optional box – pin it only when you actually use one
            Sam3Box box = new Sam3Box(1, 1, 1, 1);
            bool useBox = false;

            if (sm.Senum.HasFlag(SamEnum.Sam2Multiple))
            {
                for (int i = 0; i < boxList.Count; i++)
                {
                    box = new Sam3Box(boxList[i].X, boxList[i].Y, boxList[i].X + boxList[i].Width, boxList[i].Y + boxList[i].Height);
                    useBox = true;
                    //  Only use the Positive points that are inside the box for this iteration
                    List<Sam3Point> singlePosList = new List<Sam3Point>();
                    for (int j = 0; j < posPoints.Length; j++)
                    {
                        bool isInside = posPoints[j].X >= box.X0 && posPoints[j].X <= box.X1 && posPoints[j].Y >= box.Y0 && posPoints[j].Y <= box.Y1;

                        if (isInside)
                        {
                            singlePosList.Add(posPoints[j]);
                        }
                    }
                    Sam3Point[] singlePos = singlePosList.ToArray();
                    SamResult innerResult = ProcessSam2PvsWork(sm.Senum, useBox, isMultiMask, singlePos, negPoints, box);
                    if (innerResult.IsSuccess == false) return innerResult;
                }
            }
            else
            {
                if (boxList.Count > 0)
                {
                    box = new Sam3Box(boxList[0].X, boxList[0].Y, boxList[0].X + boxList[0].Width, boxList[0].Y + boxList[0].Height);
                    useBox = true;
                }
                SamResult innerResult = ProcessSam2PvsWork(sm.Senum, useBox, isMultiMask, posPoints, negPoints, box);
                if (innerResult.IsSuccess == false) return innerResult;
            }
            return new SamResult();
        }

        private SamResult ProcessSam2PvsWork(SamEnum se, bool useBox, bool isMultiMask, Sam3Point[] posPoints, Sam3Point[] negPoints, Sam3Box box)
        {
            GCHandle boxHandle = default;
            IntPtr boxPtr = IntPtr.Zero;
            int numPos = posPoints.Length;
            int numNeg = negPoints.Length;

            try
            {
                if (useBox)
                {
                    boxHandle = GCHandle.Alloc(box, GCHandleType.Pinned);
                    boxPtr = boxHandle.AddrOfPinnedObject();
                }

                // 4. Run segmentation
                IntPtr result = SamNative.sam3_segment_pvs_c(
                    _state,                   // from sam3_create_state_c
                    _model,                   // from sam3_load_model_c
                    posPoints,
                    posPoints?.Length ?? 0,
                    negPoints,
                    numNeg,
                    boxPtr,                        // IntPtr.Zero when no box
                    isMultiMask);

                if (result == IntPtr.Zero)
                {
                    SamResult SamStatus = new SamResult(false, "Segmentation failed");
                    return SamStatus;
                }

                int ExceptionLocation = 0;
                try
                {
                    int n = SamNative.sam3_result_num_detections(result);
                    UiMessage uimd = new UiMessage(UiEnum.SamDetection, $"Got {n} detection(s)");
                    UiMessageAction?.Invoke(uimd);

                    for (int i = 0; i < n; i++)
                    {
                        ExceptionLocation++;
                        IntPtr maskPtr = SamNative.sam3_result_mask(result, i, out int width, out int height);

                        if (maskPtr == IntPtr.Zero || width <= 0 || height <= 0) continue;

                        int size = width * height;
                        byte[] data = new byte[size];
                        Marshal.Copy(maskPtr, data, 0, size);

                        float score = SamNative.sam3_result_score(result, i);
                        float iou = SamNative.sam3_result_iou(result, i);

                        SamDetection instance = new SamDetection(se, i, data, width, height, score, iou, box);
                        UiMessage uimm = new UiMessage(UiEnum.SamDetection, "Detection", instance);
                        UiMessageAction?.Invoke(uimm);
                    }

                }
                catch (Exception ex)
                {
                    string message = "Unknown exception location";
                    if (ExceptionLocation == 0)
                    {
                        message = "Exception during Sam detection count retrieval";
                    }
                    else
                    {
                        message = "Exception during detection retrieval " + ExceptionLocation.ToString();
                    }
                    return new SamResult(false, message, ex.Message);
                }
                finally
                {
                    SamNative.sam3_free_result_c(result);
                }
                return new SamResult();
            }
            catch (Exception ex)
            {
                string message = "Exception during Sam segmentation";
                return new SamResult(false, message, ex.Message);
            }
            finally
            {
                if (boxHandle.IsAllocated)
                    boxHandle.Free();
            }

        }

        public SamResult ProcessSam3Pcs(object Ob)
        {
            IntPtr result = IntPtr.Zero;

            int ExceptionLocation = -1;
            SamMessage sm = (SamMessage)Ob;
            string textPrompt = sm.TextPrompt;
            float scoreThreshold = sm.ScoreThreshold;
            float nmsThreshold = sm.NmsThreshold;
            List<Sam3Box> boxListPositive = new List<Sam3Box>();
            List<Sam3Box> boxListNegative = new List<Sam3Box>();

            try
            {

                for (int i = 0; i < sm.Info.Count(); i++)
                {
                    if (sm.Info[i].EntryEnum.HasFlag(SamEnum.BoxPositive))
                    {
                        System.Drawing.Rectangle rect = sm.Info[i].Box;
                        Sam3Box box = new Sam3Box(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
                        boxListPositive.Add(box);
                    }
                    if (sm.Info[i].EntryEnum.HasFlag(SamEnum.BoxNegative))
                    {
                        System.Drawing.Rectangle rect = sm.Info[i].Box;
                        Sam3Box box = new Sam3Box(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
                        boxListNegative.Add(box);
                    }
                }

                if (textPrompt.Length == 0)
                {
                    SamResult SamStatus = new SamResult(false, "Sam3 Text Prompt Empty");
                    return SamStatus;
                }

                ExceptionLocation = 0;

                if ((boxListPositive.Count() > 0) && (boxListNegative.Count() > 0))
                {
                    Sam3Box[] posBoxes = boxListPositive.ToArray();
                    Sam3Box[] negBoxes = boxListNegative.ToArray();
                    result = SamNative.sam3_segment_pcs_ext_c(_state, _model, textPrompt,
                        posBoxes, posBoxes.Length, negBoxes, negBoxes.Length, scoreThreshold, nmsThreshold);
                }
                else if (boxListPositive.Count() > 0)
                {
                    Sam3Box[] posBoxes = boxListPositive.ToArray();
                    result = SamNative.sam3_segment_pcs_ext_c(_state, _model, textPrompt,
                        posBoxes, posBoxes.Length, null!, 0, scoreThreshold, nmsThreshold);
                }
                else if (boxListNegative.Count() > 0)
                {
                    Sam3Box[] negBoxes = boxListNegative.ToArray();
                    result = SamNative.sam3_segment_pcs_ext_c(_state, _model, textPrompt,
                        null!, 0, negBoxes, negBoxes.Length, scoreThreshold, nmsThreshold);
                }
                else
                {
                    result = SamNative.sam3_segment_pcs_c(_state, _model, textPrompt, scoreThreshold, nmsThreshold);
                }


                // score threshold then NMS Threshold

                if (result == IntPtr.Zero)
                {
                    SamResult SamStatus = new SamResult(false, "Sam3 Segmentation failed");
                    return SamStatus;
                }

                int n = SamNative.sam3_result_num_detections(result);
                for (int i = 0; i < n; i++)
                {
                    ExceptionLocation++;
                    IntPtr maskPtr = SamNative.sam3_result_mask(result, i, out int width, out int height);

                    int size = width * height;
                    byte[] data = new byte[size];
                    Marshal.Copy(maskPtr, data, 0, size);

                    float score = SamNative.sam3_result_score(result, i);

                    SamNative.sam3_result_box(result, i,
                        out float x0, out float y0, out float x1, out float y1);

                    Sam3Box sbox = new Sam3Box(x0, y0, x1, y1);
                    SamDetection instance = new SamDetection(SamEnum.Sam3Result, i, data, width, height, score, 0.0f, sbox);
                    instance.Label = textPrompt;
                    instance.SubLabel = "Detection " + (i + 1).ToString();
                    UiMessage uimm = new UiMessage(UiEnum.SamDetection, "Detection", instance);
                    UiMessageAction?.Invoke(uimm);
                }
                return new SamResult();
            }
            catch (Exception ex)
            {
                string message = "Exception location unknown";
                if (ExceptionLocation == -1)
                {
                    message = "Exception before Sam Segmentation call";
                }
                else if (ExceptionLocation == 0)
                {
                    message = "Exception during Sam Segmentation call";
                }
                else
                {
                    message = "Exception during detection retrieval " + ExceptionLocation.ToString();
                }
                return new SamResult(false, message, ex.Message);
            }
            finally
            {
                if (result != IntPtr.Zero) SamNative.sam3_free_result_c(result);
            }
        }


        public void Dispose()
        {
            if (_state != IntPtr.Zero)
            {
                SamNative.sam3_free_state_c(_state);
                _state = IntPtr.Zero;
            }
            if (_model != IntPtr.Zero)
            {
                SamNative.sam3_free_model_c(_model);
                _model = IntPtr.Zero;
            }
        }

    }

    public class SamResult
    {
        public bool IsSuccess;
        public bool IsException;
        public string Message = string.Empty;
        public string ExMessage = string.Empty;

        public SamResult()
        {
            IsSuccess = true;
        }

        public SamResult(bool s, string m)
        {
            IsSuccess = s;
            Message = m;
        }
        public SamResult(bool s, string m, string em)
        {
            IsSuccess = s;
            IsException = true;
            Message = m;
            ExMessage = em;
        }
    }

    public class SamDetection
    {
        public SamEnum Senum;
        public int Index;
        public byte[] Data;
        public int ImageHeight;
        public int ImageWidth;
        public float Score;
        public float Iou;
        public System.Drawing.Rectangle Rect;
        public string Label = String.Empty;
        public string SubLabel = String.Empty;

        public SamDetection(SamEnum se, int ind, Byte[] data, int imageWidth, int imageHeight, float s, float iou, Sam3Box? box)
        {
            Senum = se;
            Index = ind;
            Score = s;
            Iou = iou;
            Data = data;
            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
            if ((box is null) || (box.Value.X1 == 0))
            {
                Rect = new System.Drawing.Rectangle(0, 0, 0, 0);
            }
            else
            {
                Rect = new System.Drawing.Rectangle((int)box.Value.X0, (int)box.Value.Y0,
                    (int)(box.Value.X1 - box.Value.X0), (int)(box.Value.Y1 - box.Value.Y0));
            }
        }
    }
    public class SamMessage
    {
        public SamEnum Senum;
        public List<SamInfo> Info;
        public string TextPrompt = String.Empty;
        public float ScoreThreshold = 0.5f;
        public float NmsThreshold = 0.1f;

        public SamMessage()
        {
            Senum = SamEnum.None;
            Info = new List<SamInfo>();
        }

        public SamMessage(SamEnum se, List<SamInfo> info)
        {
            Senum = se;
            Info = info;
        }
        public SamMessage(SamEnum se, List<SamInfo> info, string textPrompt, float scoreThreshold, float nmsThreshold)
        {
            Senum = se;
            Info = info;
            TextPrompt = textPrompt;
            ScoreThreshold = scoreThreshold;
            NmsThreshold = nmsThreshold;
        }
    }
    public class SamInfo
    {
        public SamEnum EntryEnum;
        public int X;
        public int Y;
        public System.Drawing.Rectangle Box;

        public SamInfo()
        {
            EntryEnum = SamEnum.None;
            Box = new System.Drawing.Rectangle();
        }

        public SamInfo(SamEnum se, int x, int y)
        {
            EntryEnum = se;
            X = x;
            Y = y;
            Box = new System.Drawing.Rectangle();
        }
        public SamInfo(SamEnum se, int x, int y, int xx, int yy)
        {
            EntryEnum = se;
            X = x;
            Y = y;
            Box = new System.Drawing.Rectangle();
        }

        public SamInfo(SamEnum se, Rectangle r)
        {
            EntryEnum = se;
            Box = r;
        }
    }
}
