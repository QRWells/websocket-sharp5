// ReSharper disable CommentTypo

#region License

/*
 * WebSocketFrame.cs
 *
 * The MIT License
 *
 * Copyright (c) 2012-2019 sta.blockhead
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

#endregion

#region Contributors

/*
 * Contributors:
 * - Chris Swiedler
 */

#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

// ReSharper disable MemberCanBePrivate.Global

namespace WebSocketSharp
{
    internal class WebSocketFrame : IEnumerable<byte>
    {
        #region Internal Fields

        /// <summary>
        ///     Represents the ping frame without the payload data as an array of
        ///     <see cref="byte" />.
        /// </summary>
        /// <remarks>
        ///     The value of this field is created from a non masked ping frame,
        ///     so it can only be used to send a ping from the server.
        /// </remarks>
        internal static readonly byte[] EmptyPingBytes;

        #endregion

        #region Static Constructor

        static WebSocketFrame()
        {
            EmptyPingBytes = CreatePingFrame(false).ToArray();
        }

        #endregion

        #region Private Constructors

        private WebSocketFrame()
        {
        }

        #endregion

        #region Explicit Interface Implementations

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #region Private Fields

        #endregion

        #region Internal Constructors

        internal WebSocketFrame(Opcode opcode, PayloadData payloadData, bool mask)
            : this(Fin.Final, opcode, payloadData, false, mask)
        {
        }

        internal WebSocketFrame(
            Fin fin, Opcode opcode, byte[] data, bool compressed, bool mask
        )
            : this(fin, opcode, new PayloadData(data), compressed, mask)
        {
        }

        internal WebSocketFrame(
            Fin fin,
            Opcode opcode,
            PayloadData payloadData,
            bool compressed,
            bool mask
        )
        {
            Fin = fin;
            Opcode = opcode;

            Rsv1 = opcode.IsData() && compressed ? Rsv.On : Rsv.Off;
            Rsv2 = Rsv.Off;
            Rsv3 = Rsv.Off;

            var len = payloadData.Length;
            if (len < 126)
            {
                PayloadLength = (byte) len;
                ExtendedPayloadLength = WebSocket.EmptyBytes;
            }
            else if (len < 0x010000)
            {
                PayloadLength = 126;
                ExtendedPayloadLength = ((ushort) len).InternalToByteArray(ByteOrder.Big);
            }
            else
            {
                PayloadLength = 127;
                ExtendedPayloadLength = len.InternalToByteArray(ByteOrder.Big);
            }

            if (mask)
            {
                Mask = Mask.On;
                MaskingKey = createMaskingKey();
                payloadData.Mask(MaskingKey);
            }
            else
            {
                Mask = Mask.Off;
                MaskingKey = WebSocket.EmptyBytes;
            }

            PayloadData = payloadData;
        }

        #endregion

        #region Internal Properties

        internal ulong ExactPayloadLength =>
            PayloadLength < 126
                ? PayloadLength
                : PayloadLength == 126
                    ? ExtendedPayloadLength.ToUInt16(ByteOrder.Big)
                    : ExtendedPayloadLength.ToUInt64(ByteOrder.Big);

        internal int ExtendedPayloadLengthWidth =>
            PayloadLength < 126
                ? 0
                : PayloadLength == 126
                    ? 2
                    : 8;

        #endregion

        #region Public Properties

        public byte[] ExtendedPayloadLength { get; private set; }

        public Fin Fin { get; private set; }

        public bool IsBinary => Opcode == Opcode.Binary;

        public bool IsClose => Opcode == Opcode.Close;

        public bool IsCompressed => Rsv1 == Rsv.On;

        public bool IsContinuation => Opcode == Opcode.Cont;

        public bool IsControl => Opcode >= Opcode.Close;

        public bool IsData => Opcode == Opcode.Text || Opcode == Opcode.Binary;

        public bool IsFinal => Fin == Fin.Final;

        public bool IsFragment => Fin == Fin.More || Opcode == Opcode.Cont;

        public bool IsMasked => Mask == Mask.On;

        public bool IsPing => Opcode == Opcode.Ping;

        public bool IsPong => Opcode == Opcode.Pong;

        public bool IsText => Opcode == Opcode.Text;

        public ulong Length =>
            2
            + (ulong) (ExtendedPayloadLength.Length + MaskingKey.Length)
            + PayloadData.Length;

        public Mask Mask { get; private set; }

        public byte[] MaskingKey { get; private set; }

        public Opcode Opcode { get; private set; }

        public PayloadData PayloadData { get; private set; }

        public byte PayloadLength { get; private set; }

        public Rsv Rsv1 { get; private set; }

        public Rsv Rsv2 { get; private set; }

        public Rsv Rsv3 { get; private set; }

        #endregion

        #region Private Methods

        private static byte[] createMaskingKey()
        {
            var key = new byte[4];
            WebSocket.RandomNumber.GetBytes(key);

            return key;
        }

        private static string dump(WebSocketFrame frame)
        {
            var len = frame.Length;
            var cnt = (long) (len / 4);
            var rem = (int) (len % 4);

            int cntDigit;
            string cntFmt;
            switch (cnt)
            {
                case < 10000:
                    cntDigit = 4;
                    cntFmt = "{0,4}";
                    break;
                case < 0x010000:
                    cntDigit = 4;
                    cntFmt = "{0,4:X}";
                    break;
                case < 0x0100000000:
                    cntDigit = 8;
                    cntFmt = "{0,8:X}";
                    break;
                default:
                    cntDigit = 16;
                    cntFmt = "{0,16:X}";
                    break;
            }

            var spFmt = $"{{0,{cntDigit}}}";

            var headerFmt = string.Format(
                @"
{0} 01234567 89ABCDEF 01234567 89ABCDEF
{0}+--------+--------+--------+--------+\n",
                spFmt
            );

            var lineFmt = $"{cntFmt}|{{1,8}} {{2,8}} {{3,8}} {{4,8}}|\n";

            var footerFmt = $"{spFmt}+--------+--------+--------+--------+";

            var buff = new StringBuilder(64);

            Action<string, string, string, string> LinePrinter()
            {
                long lineCnt = 0;
                return (arg1, arg2, arg3, arg4) => { buff.AppendFormat(lineFmt, ++lineCnt, arg1, arg2, arg3, arg4); };
            }

            var printLine = LinePrinter();
            var bytes = frame.ToArray();

            buff.AppendFormat(headerFmt, string.Empty);

            for (long i = 0; i <= cnt; i++)
            {
                var j = i * 4;

                if (i < cnt)
                {
                    printLine(
                        Convert.ToString(bytes[j], 2).PadLeft(8, '0'),
                        Convert.ToString(bytes[j + 1], 2).PadLeft(8, '0'),
                        Convert.ToString(bytes[j + 2], 2).PadLeft(8, '0'),
                        Convert.ToString(bytes[j + 3], 2).PadLeft(8, '0')
                    );

                    continue;
                }

                if (rem > 0)
                    printLine(
                        Convert.ToString(bytes[j], 2).PadLeft(8, '0'),
                        rem >= 2
                            ? Convert.ToString(bytes[j + 1], 2).PadLeft(8, '0')
                            : string.Empty,
                        rem == 3
                            ? Convert.ToString(bytes[j + 2], 2).PadLeft(8, '0')
                            : string.Empty,
                        string.Empty
                    );
            }

            buff.AppendFormat(footerFmt, string.Empty);
            return buff.ToString();
        }

        private static string print(WebSocketFrame frame)
        {
            // Payload Length
            var payloadLen = frame.PayloadLength;

            // Extended Payload Length
            var extPayloadLen = payloadLen > 125
                ? frame.ExactPayloadLength.ToString()
                : string.Empty;

            // Masking Key
            var maskingKey = BitConverter.ToString(frame.MaskingKey);

            // Payload Data
            var payload = payloadLen == 0
                ? string.Empty
                : payloadLen > 125
                    ? "---"
                    : !frame.IsText
                      || frame.IsFragment
                      || frame.IsMasked
                      || frame.IsCompressed
                        ? frame.PayloadData.ToString()
                        : utf8Decode(frame.PayloadData.ApplicationData);

            const string fmt = @"
                    FIN: {0}
                   RSV1: {1}
                   RSV2: {2}
                   RSV3: {3}
                 Opcode: {4}
                   MASK: {5}
         Payload Length: {6}
Extended Payload Length: {7}
            Masking Key: {8}
           Payload Data: {9}";

            return string.Format(
                fmt,
                frame.Fin,
                frame.Rsv1,
                frame.Rsv2,
                frame.Rsv3,
                frame.Opcode,
                frame.Mask,
                payloadLen,
                extPayloadLen,
                maskingKey,
                payload
            );
        }

        private static WebSocketFrame processHeader(byte[] header)
        {
            if (header.Length != 2)
            {
                const string msg = "The header part of a frame could not be read.";
                throw new WebSocketException(msg);
            }

            // FIN
            var fin = (header[0] & 0x80) == 0x80 ? Fin.Final : Fin.More;

            // RSV1
            var rsv1 = (header[0] & 0x40) == 0x40 ? Rsv.On : Rsv.Off;

            // RSV2
            var rsv2 = (header[0] & 0x20) == 0x20 ? Rsv.On : Rsv.Off;

            // RSV3
            var rsv3 = (header[0] & 0x10) == 0x10 ? Rsv.On : Rsv.Off;

            // Opcode
            var opcode = (byte) (header[0] & 0x0f);

            // MASK
            var mask = (header[1] & 0x80) == 0x80 ? Mask.On : Mask.Off;

            // Payload Length
            var payloadLen = (byte) (header[1] & 0x7f);

            if (!opcode.IsSupported())
            {
                const string msg = "A frame has an unsupported opcode.";
                throw new WebSocketException(CloseStatusCode.ProtocolError, msg);
            }

            if (!opcode.IsData() && rsv1 == Rsv.On)
            {
                const string msg = "A non data frame is compressed.";
                throw new WebSocketException(CloseStatusCode.ProtocolError, msg);
            }

            if (opcode.IsControl())
            {
                if (fin == Fin.More)
                {
                    const string msg = "A control frame is fragmented.";
                    throw new WebSocketException(CloseStatusCode.ProtocolError, msg);
                }

                if (payloadLen > 125)
                {
                    const string msg = "A control frame has too long payload length.";
                    throw new WebSocketException(CloseStatusCode.ProtocolError, msg);
                }
            }

            var frame = new WebSocketFrame
            {
                Fin = fin,
                Rsv1 = rsv1,
                Rsv2 = rsv2,
                Rsv3 = rsv3,
                Opcode = (Opcode) opcode,
                Mask = mask,
                PayloadLength = payloadLen
            };

            return frame;
        }

        private static void readExtendedPayloadLength(Stream stream, WebSocketFrame frame)
        {
            var len = frame.ExtendedPayloadLengthWidth;
            if (len == 0)
            {
                frame.ExtendedPayloadLength = WebSocket.EmptyBytes;
                return;
            }

            var bytes = stream.ReadBytes(len);
            if (bytes.Length != len)
            {
                const string msg = "The extended payload length of a frame could not be read.";
                throw new WebSocketException(msg);
            }

            frame.ExtendedPayloadLength = bytes;
        }

        private static void readExtendedPayloadLengthAsync(
            Stream stream,
            WebSocketFrame frame,
            Action<WebSocketFrame> completed,
            Action<Exception> error
        )
        {
            var len = frame.ExtendedPayloadLengthWidth;
            if (len == 0)
            {
                frame.ExtendedPayloadLength = WebSocket.EmptyBytes;
                completed(frame);

                return;
            }

            stream.ReadBytesAsync(
                len,
                bytes =>
                {
                    if (bytes.Length != len)
                    {
                        const string msg = "The extended payload length of a frame could not be read.";
                        throw new WebSocketException(msg);
                    }

                    frame.ExtendedPayloadLength = bytes;
                    completed(frame);
                },
                error
            );
        }

        private static WebSocketFrame readHeader(Stream stream)
        {
            return processHeader(stream.ReadBytes(2));
        }

        private static void readHeaderAsync(
            Stream stream, Action<WebSocketFrame> completed, Action<Exception> error
        )
        {
            stream.ReadBytesAsync(
                2, bytes => completed(processHeader(bytes)), error
            );
        }

        private static void readMaskingKey(Stream stream, WebSocketFrame frame)
        {
            if (!frame.IsMasked)
            {
                frame.MaskingKey = WebSocket.EmptyBytes;
                return;
            }

            const int len = 4;
            var bytes = stream.ReadBytes(len);

            if (bytes.Length != len)
            {
                const string msg = "The masking key of a frame could not be read.";
                throw new WebSocketException(msg);
            }

            frame.MaskingKey = bytes;
        }

        private static void readMaskingKeyAsync(
            Stream stream,
            WebSocketFrame frame,
            Action<WebSocketFrame> completed,
            Action<Exception> error
        )
        {
            if (!frame.IsMasked)
            {
                frame.MaskingKey = WebSocket.EmptyBytes;
                completed(frame);

                return;
            }

            const int len = 4;

            stream.ReadBytesAsync(
                len,
                bytes =>
                {
                    if (bytes.Length != len)
                    {
                        const string msg = "The masking key of a frame could not be read.";
                        throw new WebSocketException(msg);
                    }

                    frame.MaskingKey = bytes;
                    completed(frame);
                },
                error
            );
        }

        private static void readPayloadData(Stream stream, WebSocketFrame frame)
        {
            var exactLen = frame.ExactPayloadLength;
            if (exactLen > PayloadData.MaxLength)
            {
                const string msg = "A frame has too long payload length.";
                throw new WebSocketException(CloseStatusCode.TooBig, msg);
            }

            if (exactLen == 0)
            {
                frame.PayloadData = PayloadData.Empty;
                return;
            }

            var len = (long) exactLen;
            var bytes = frame.PayloadLength < 127
                ? stream.ReadBytes((int) exactLen)
                : stream.ReadBytes(len, 1024);

            if (bytes.LongLength != len)
            {
                const string msg = "The payload data of a frame could not be read.";
                throw new WebSocketException(msg);
            }

            frame.PayloadData = new PayloadData(bytes, len);
        }

        private static void readPayloadDataAsync(
            Stream stream,
            WebSocketFrame frame,
            Action<WebSocketFrame> completed,
            Action<Exception> error
        )
        {
            var exactLen = frame.ExactPayloadLength;
            if (exactLen > PayloadData.MaxLength)
            {
                const string msg = "A frame has too long payload length.";
                throw new WebSocketException(CloseStatusCode.TooBig, msg);
            }

            if (exactLen == 0)
            {
                frame.PayloadData = PayloadData.Empty;
                completed(frame);

                return;
            }

            var len = (long) exactLen;

            void Comp(byte[] bytes)
            {
                if (bytes.LongLength != len)
                {
                    const string msg = "The payload data of a frame could not be read.";
                    throw new WebSocketException(msg);
                }

                frame.PayloadData = new PayloadData(bytes, len);
                completed(frame);
            }

            if (frame.PayloadLength < 127)
            {
                stream.ReadBytesAsync((int) exactLen, Comp, error);
                return;
            }

            stream.ReadBytesAsync(len, 1024, Comp, error);
        }

        private static string utf8Decode(byte[] bytes)
        {
            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Internal Methods

        internal static WebSocketFrame CreateCloseFrame(
            PayloadData payloadData, bool mask
        )
        {
            return new(
                Fin.Final, Opcode.Close, payloadData, false, mask
            );
        }

        internal static WebSocketFrame CreatePingFrame(bool mask)
        {
            return new(
                Fin.Final, Opcode.Ping, PayloadData.Empty, false, mask
            );
        }

        internal static WebSocketFrame CreatePingFrame(byte[] data, bool mask)
        {
            return new(
                Fin.Final, Opcode.Ping, new PayloadData(data), false, mask
            );
        }

        internal static WebSocketFrame CreatePongFrame(
            PayloadData payloadData, bool mask
        )
        {
            return new(
                Fin.Final, Opcode.Pong, payloadData, false, mask
            );
        }

        internal static WebSocketFrame ReadFrame(Stream stream, bool unmask)
        {
            var frame = readHeader(stream);
            readExtendedPayloadLength(stream, frame);
            readMaskingKey(stream, frame);
            readPayloadData(stream, frame);

            if (unmask)
                frame.Unmask();

            return frame;
        }

        internal static void ReadFrameAsync(
            Stream stream,
            bool unmask,
            Action<WebSocketFrame> completed,
            Action<Exception> error
        )
        {
            readHeaderAsync(
                stream,
                frame =>
                    readExtendedPayloadLengthAsync(
                        stream,
                        frame,
                        frame1 =>
                            readMaskingKeyAsync(
                                stream,
                                frame1,
                                frame2 =>
                                    readPayloadDataAsync(
                                        stream,
                                        frame2,
                                        frame3 =>
                                        {
                                            if (unmask)
                                                frame3.Unmask();

                                            completed(frame3);
                                        },
                                        error
                                    ),
                                error
                            ),
                        error
                    ),
                error
            );
        }

        internal void Unmask()
        {
            if (Mask == Mask.Off)
                return;

            Mask = Mask.Off;
            PayloadData.Mask(MaskingKey);
            MaskingKey = WebSocket.EmptyBytes;
        }

        #endregion

        #region Public Methods

        public IEnumerator<byte> GetEnumerator()
        {
            return ((IEnumerable<byte>) ToArray()).GetEnumerator();
        }

        public void Print(bool dumped)
        {
            Console.WriteLine(dumped ? dump(this) : print(this));
        }

        public string PrintToString(bool dumped)
        {
            return dumped ? dump(this) : print(this);
        }

        public byte[] ToArray()
        {
            using var buff = new MemoryStream();
            var header = (int) Fin;
            header = (header << 1) + (int) Rsv1;
            header = (header << 1) + (int) Rsv2;
            header = (header << 1) + (int) Rsv3;
            header = (header << 4) + (int) Opcode;
            header = (header << 1) + (int) Mask;
            header = (header << 7) + PayloadLength;

            buff.Write(
                ((ushort) header).InternalToByteArray(ByteOrder.Big), 0, 2
            );

            if (PayloadLength > 125)
                buff.Write(ExtendedPayloadLength, 0, PayloadLength == 126 ? 2 : 8);

            if (Mask == Mask.On)
                buff.Write(MaskingKey, 0, 4);

            if (PayloadLength > 0)
            {
                var bytes = PayloadData.ToArray();

                if (PayloadLength < 127)
                    buff.Write(bytes, 0, bytes.Length);
                else
                    buff.WriteBytes(bytes, 1024);
            }

            buff.Close();
            return buff.ToArray();
        }

        public override string ToString()
        {
            return BitConverter.ToString(ToArray());
        }

        #endregion
    }
}