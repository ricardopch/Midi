using System;
using System.Runtime.InteropServices;
using MelodiaMidi.Engine;

namespace MelodiaMidi
{
    /**
     * Lectura y escritura de audio con Media Foundation (incluido en Windows 7 y posteriores):
     * decodifica MP3, M4A/AAC, WMA, WAV, FLAC, ALAC, vídeos MP4/MKV... y codifica AAC en un MP4.
     * Interop COM propio; el orden de los métodos replica las cabeceras de Windows (mfobjects.h, mfreadwrite.h).
     */
    internal static class MediaFoundation
    {
        public sealed class MfException : Exception
        {
            public readonly int HResult32;

            public MfException(string what, int hr) : base(Describe(what, hr)) { HResult32 = hr; }

            private static string Describe(string what, int hr)
            {
                uint u = unchecked((uint)hr);
                string reason;
                switch (u)
                {
                    case 0xC00D36C4: reason = "formato de archivo no reconocido"; break;           // MF_E_UNSUPPORTED_BYTESTREAM_TYPE
                    case 0xC00D36B4: reason = "formato no soportado"; break;                       // MF_E_INVALIDMEDIATYPE
                    case 0xC00D5212: reason = "no hay códec instalado para este audio"; break;     // MF_E_TOPO_CODEC_NOT_FOUND
                    case 0xC00D36B9: reason = "no hay ningún formato compatible"; break;           // MF_E_NO_MORE_TYPES
                    case 0xC00D6D6F: reason = "no hay códec que haga esta conversión"; break;     // MF_E_TRANSFORM_NOT_POSSIBLE_FOR_CURRENT_MEDIATYPE_COMBINATION
                    case 0xC00D36D5: reason = "no se encontró"; break;                             // MF_E_NOT_FOUND
                    case 0x80070002: reason = "archivo no encontrado"; break;
                    case 0x80070005: reason = "acceso denegado"; break;
                    case 0x80070020: reason = "el archivo está en uso por otro programa"; break;
                    default: reason = "error 0x" + u.ToString("X8"); break;
                }
                return what + ": " + reason + ".";
            }
        }

        /** Windows sin Media Foundation (ediciones «N» sin «Media Feature Pack»). */
        public sealed class MfUnavailableException : Exception
        {
            public MfUnavailableException() : base("Este Windows no tiene los componentes multimedia (Media Foundation). "
                + "En las ediciones «N» de Windows hay que instalar el «Media Feature Pack». Mientras tanto puedes usar archivos WAV.") { }
        }


        // ------------------------------------------------------------------ constantes

        private const int MF_VERSION = 0x00020070;
        private const int MFSTARTUP_FULL = 0;
        private const int MF_SOURCE_READER_ALL_STREAMS = unchecked((int)0xFFFFFFFE);
        private const int MF_SOURCE_READER_FIRST_AUDIO_STREAM = unchecked((int)0xFFFFFFFD);
        private const int MF_SOURCE_READER_MEDIASOURCE = unchecked((int)0xFFFFFFFF);
        private const int MF_SOURCE_READERF_ERROR = 0x1;
        private const int MF_SOURCE_READERF_ENDOFSTREAM = 0x2;
        private const int MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED = 0x20;
        private const int MF_E_ATTRIBUTENOTFOUND = unchecked((int)0xC00D36E6);
        private const int MF_E_NOT_FOUND = unchecked((int)0xC00D36D5);

        internal static readonly Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        internal static readonly Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        internal static readonly Guid MF_MT_AUDIO_NUM_CHANNELS = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        internal static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        internal static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
        internal static readonly Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");
        internal static readonly Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731");
        internal static readonly Guid MF_PD_DURATION = new Guid("6c990d33-bb8e-477a-8598-0d5d96fcd88a");
        internal static readonly Guid MFMediaType_Audio = new Guid("73647561-0000-0010-8000-00aa00389b71");
        internal static readonly Guid MFAudioFormat_PCM = new Guid("00000001-0000-0010-8000-00aa00389b71");
        internal static readonly Guid MFAudioFormat_Float = new Guid("00000003-0000-0010-8000-00aa00389b71");

        // ------------------------------------------------------------------ interfaces COM

        [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMFMediaType
        {
            // IMFAttributes
            void GetItem();
            void GetItemType();
            void CompareItem();
            void Compare();
            [PreserveSig] int GetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out int punValue);
            [PreserveSig] int GetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out long punValue);
            void GetDouble();
            [PreserveSig] int GetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out Guid pguidValue);
            void GetStringLength();
            void GetString();
            void GetAllocatedString();
            void GetBlobSize();
            void GetBlob();
            void GetAllocatedBlob();
            void GetUnknown();
            void SetItem();
            void DeleteItem();
            void DeleteAllItems();
            [PreserveSig] int SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, int unValue);
            void SetUINT64();
            void SetDouble();
            [PreserveSig] int SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [In, MarshalAs(UnmanagedType.LPStruct)] Guid guidValue);
            void SetString();
            void SetBlob();
            void SetUnknown();
            void LockStore();
            void UnlockStore();
            void GetCount();
            void GetItemByIndex();
            void CopyAllItems();
            // IMFMediaType
            void GetMajorType();
            void IsCompressedFormat();
            void IsEqual();
            void GetRepresentation();
            void FreeRepresentation();
        }

        [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMFSample
        {
            // IMFAttributes
            void GetItem();
            void GetItemType();
            void CompareItem();
            void Compare();
            void GetUINT32();
            void GetUINT64();
            void GetDouble();
            void GetGUID();
            void GetStringLength();
            void GetString();
            void GetAllocatedString();
            void GetBlobSize();
            void GetBlob();
            void GetAllocatedBlob();
            void GetUnknown();
            void SetItem();
            void DeleteItem();
            void DeleteAllItems();
            void SetUINT32();
            void SetUINT64();
            void SetDouble();
            void SetGUID();
            void SetString();
            void SetBlob();
            void SetUnknown();
            void LockStore();
            void UnlockStore();
            void GetCount();
            void GetItemByIndex();
            void CopyAllItems();
            // IMFSample
            void GetSampleFlags();
            void SetSampleFlags();
            void GetSampleTime();
            [PreserveSig] int SetSampleTime(long hnsSampleTime);
            void GetSampleDuration();
            [PreserveSig] int SetSampleDuration(long hnsSampleDuration);
            void GetBufferCount();
            void GetBufferByIndex();
            [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
            [PreserveSig] int AddBuffer(IMFMediaBuffer pBuffer);
            void RemoveBufferByIndex();
            void RemoveAllBuffers();
            void GetTotalLength();
            void CopyToBuffer();
        }

        [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMFMediaBuffer
        {
            [PreserveSig] int Lock(out IntPtr ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);
            [PreserveSig] int Unlock();
            [PreserveSig] int GetCurrentLength(out int pcbCurrentLength);
            [PreserveSig] int SetCurrentLength(int cbCurrentLength);
            [PreserveSig] int GetMaxLength(out int pcbMaxLength);
        }


        [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMFSourceReader
        {
            void GetStreamSelection();
            [PreserveSig] int SetStreamSelection(int dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] bool fSelected);
            void GetNativeMediaType();
            [PreserveSig] int GetCurrentMediaType(int dwStreamIndex, out IMFMediaType ppMediaType);
            [PreserveSig] int SetCurrentMediaType(int dwStreamIndex, IntPtr pdwReserved, IMFMediaType pMediaType);
            void SetCurrentPosition();
            [PreserveSig] int ReadSample(int dwStreamIndex, int dwControlFlags, out int pdwActualStreamIndex,
                                         out int pdwStreamFlags, out long pllTimestamp, out IMFSample ppSample);
            void Flush();
            void GetServiceForStream();
            [PreserveSig] int GetPresentationAttribute(int dwStreamIndex, [In, MarshalAs(UnmanagedType.LPStruct)] Guid guidAttribute, IntPtr pvarAttribute);
        }


        // ------------------------------------------------------------------ funciones

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFStartup(int version, int dwFlags);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFShutdown();

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateMediaType(out IMFMediaType ppMFType);




        [DllImport("mfreadwrite.dll", ExactSpelling = true)]
        private static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string pwszURL, IntPtr pAttributes,
                                                              out IMFSourceReader ppSourceReader);


        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern int PropVariantClear(IntPtr pvar);

        private static void Check(int hr, string what)
        {
            if (hr < 0) throw new MfException(what, hr);
        }

        private static void Release(object o)
        {
            if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
        }

        private static void Startup()
        {
            int hr;
            try
            {
                hr = MFStartup(MF_VERSION, MFSTARTUP_FULL);
            }
            catch (DllNotFoundException)
            {
                throw new MfUnavailableException();
            }
            catch (EntryPointNotFoundException)
            {
                throw new MfUnavailableException();
            }
            Check(hr, "No se pudo iniciar Media Foundation");
        }

        // ------------------------------------------------------------------ decodificar

        public sealed class Decoded
        {
            public float[] Samples;      // mono [-1, 1]
            public int Length;
            public int SampleRate;
            public bool Truncated;
        }

        public static Decoded Decode(string path, int maxSeconds, IProgress p)
        {
            Startup();
            IMFSourceReader reader = null;
            try
            {
                Check(MFCreateSourceReaderFromURL(path, IntPtr.Zero, out reader), "No se pudo abrir el archivo");
                Check(reader.SetStreamSelection(MF_SOURCE_READER_ALL_STREAMS, false), "Error al preparar la lectura");
                Check(reader.SetStreamSelection(MF_SOURCE_READER_FIRST_AUDIO_STREAM, true), "El archivo no contiene audio");

                // pedir PCM de 16 bits; si el decodificador no puede, PCM float
                int hr = SetPcmOutput(reader, MFAudioFormat_PCM);
                if (hr < 0) hr = SetPcmOutput(reader, MFAudioFormat_Float);
                Check(hr, "No se puede decodificar este audio");

                Format fmt = ReadFormat(reader);
                long durHns = Duration(reader);
                long maxFrames = (long)maxSeconds * fmt.Rate;
                float[] buf = new float[(int)Math.Min(Math.Max(durHns, 0) / 10000000L * fmt.Rate + fmt.Rate, maxFrames + fmt.Rate) + 4096];
                int count = 0;
                bool truncated = false;
                byte[] bytes = new byte[0];
                while (true)
                {
                    if (p.isCancelled()) throw new CancelledException();
                    int idx, flags;
                    long ts;
                    IMFSample sample;
                    Check(reader.ReadSample(MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0, out idx, out flags, out ts, out sample), "Error al leer el audio");
                    try
                    {
                        if ((flags & MF_SOURCE_READERF_ERROR) != 0) throw new MfException("Error al decodificar el audio", unchecked((int)0x80004005));
                        if ((flags & MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0)
                        {
                            Format nf = ReadFormat(reader);
                            if (nf.Rate != fmt.Rate) throw new MfException("El audio cambia de frecuencia a mitad", unchecked((int)0x80004005));
                            fmt = nf;
                        }
                        if (sample != null)
                        {
                            IMFMediaBuffer mb;
                            Check(sample.ConvertToContiguousBuffer(out mb), "Error al leer el audio");
                            try
                            {
                                IntPtr ptr;
                                int max, len;
                                Check(mb.Lock(out ptr, out max, out len), "Error al leer el audio");
                                try
                                {
                                    if (bytes.Length < len) bytes = new byte[len];
                                    Marshal.Copy(ptr, bytes, 0, len);
                                }
                                finally
                                {
                                    mb.Unlock();
                                }
                                int frames = len / fmt.FrameBytes;
                                if (count + frames > buf.Length)
                                {
                                    float[] nb = new float[Math.Max(buf.Length + buf.Length / 2, count + frames)];
                                    Array.Copy(buf, nb, count);
                                    buf = nb;
                                }
                                count += ToMono(bytes, frames, fmt, buf, count);
                            }
                            finally
                            {
                                Release(mb);
                            }
                            if (durHns > 0) p.update((float)Math.Min(1.0, ts / (double)durHns));
                        }
                    }
                    finally
                    {
                        Release(sample);
                    }
                    if ((flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0) break;
                    if (count >= maxFrames)
                    {
                        truncated = true;
                        count = (int)maxFrames;
                        break;
                    }
                }
                if (count == 0) throw new MfException("No se ha podido leer audio del archivo", unchecked((int)0x80004005));
                return new Decoded { Samples = buf, Length = count, SampleRate = fmt.Rate, Truncated = truncated };
            }
            finally
            {
                Release(reader);
                MFShutdown();
            }
        }

        private sealed class Format
        {
            public int Channels, Rate, Bits, FrameBytes;
            public bool IsFloat;
        }

        private static int SetPcmOutput(IMFSourceReader reader, Guid subtype)
        {
            IMFMediaType mt;
            Check(MFCreateMediaType(out mt), "Error de Media Foundation");
            try
            {
                Check(mt.SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio), "Error de Media Foundation");
                Check(mt.SetGUID(MF_MT_SUBTYPE, subtype), "Error de Media Foundation");
                return reader.SetCurrentMediaType(MF_SOURCE_READER_FIRST_AUDIO_STREAM, IntPtr.Zero, mt);
            }
            finally
            {
                Release(mt);
            }
        }

        private static Format ReadFormat(IMFSourceReader reader)
        {
            IMFMediaType cur;
            Check(reader.GetCurrentMediaType(MF_SOURCE_READER_FIRST_AUDIO_STREAM, out cur), "Error al leer el formato");
            try
            {
                Format f = new Format();
                Guid sub;
                Check(cur.GetGUID(MF_MT_SUBTYPE, out sub), "Error al leer el formato");
                Check(cur.GetUINT32(MF_MT_AUDIO_NUM_CHANNELS, out f.Channels), "Error al leer el formato");
                Check(cur.GetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, out f.Rate), "Error al leer el formato");
                Check(cur.GetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, out f.Bits), "Error al leer el formato");
                f.IsFloat = sub == MFAudioFormat_Float;
                if (f.Channels < 1 || f.Rate < 1000) throw new MfException("Formato de audio no válido", unchecked((int)0x80004005));
                if (!(f.IsFloat ? f.Bits == 32 : (f.Bits == 8 || f.Bits == 16 || f.Bits == 24 || f.Bits == 32)))
                    throw new MfException("Formato de audio no soportado (" + f.Bits + " bits)", unchecked((int)0x80004005));
                f.FrameBytes = f.Channels * f.Bits / 8;
                return f;
            }
            finally
            {
                Release(cur);
            }
        }

        private static long Duration(IMFSourceReader reader)
        {
            IntPtr pv = Marshal.AllocCoTaskMem(32);
            try
            {
                for (int i = 0; i < 32; i++) Marshal.WriteByte(pv, i, 0);
                if (reader.GetPresentationAttribute(MF_SOURCE_READER_MEDIASOURCE, MF_PD_DURATION, pv) < 0) return -1;
                short vt = Marshal.ReadInt16(pv);
                long v = vt == 21 /* VT_UI8 */ || vt == 20 /* VT_I8 */ ? Marshal.ReadInt64(pv, 8) : -1;
                PropVariantClear(pv);
                return v;
            }
            catch (Exception)
            {
                return -1;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pv);
            }
        }

        private static int ToMono(byte[] b, int frames, Format f, float[] dst, int off)
        {
            int ch = f.Channels;
            int bps = f.Bits / 8;
            float inv = 1f / ch;
            for (int i = 0; i < frames; i++)
            {
                int k = i * f.FrameBytes;
                float sum = 0;
                for (int c = 0; c < ch; c++, k += bps)
                {
                    float v;
                    if (f.IsFloat) v = BitConverter.ToSingle(b, k);
                    else if (bps == 2) v = BitConverter.ToInt16(b, k) / 32768f;
                    else if (bps == 1) v = (b[k] - 128) / 128f;
                    else if (bps == 3) v = (b[k] | (b[k + 1] << 8) | ((sbyte)b[k + 2] << 16)) / 8388608f;
                    else v = BitConverter.ToInt32(b, k) / 2147483648f;
                    sum += v;
                }
                float m = sum * inv;
                if (m > 1f) m = 1f;
                if (m < -1f) m = -1f;
                dst[off + i] = m;
            }
            return frames;
        }
    }
}
