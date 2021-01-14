#region License

/*
 * ChunkedRequestStream.cs
 *
 * This code is derived from ChunkedInputStream.cs (System.Net) of Mono
 * (http://www.mono-project.com).
 *
 * The MIT License
 *
 * Copyright (c) 2005 Novell, Inc. (http://www.novell.com)
 * Copyright (c) 2012-2015 sta.blockhead
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

#region Authors

/*
 * Authors:
 * - Gonzalo Paniagua Javier <gonzalo@novell.com>
 */

#endregion

using System;
using System.IO;
// ReSharper disable IdentifierTypo

namespace WebSocketSharp.Net
{
    internal class ChunkedRequestStream : RequestStream
    {
        #region Internal Constructors

        internal ChunkedRequestStream(
            Stream stream, byte[] buffer, int offset, int count, HttpListenerContext context)
            : base(stream, buffer, offset, count)
        {
            _context = context;
            Decoder = new ChunkStream((WebHeaderCollection) context.Request.Headers);
        }

        #endregion

        #region Internal Properties

        internal ChunkStream Decoder { get; set; }

        #endregion

        #region Private Methods

        private void onRead(IAsyncResult asyncResult)
        {
            var rstate = (ReadBufferState) asyncResult.AsyncState;
            if (rstate == null) return;
            var ares = rstate.AsyncResult;
            try
            {
                var nread = base.EndRead(asyncResult);
                Decoder.Write(ares.Buffer, ares.Offset, nread);
                nread = Decoder.Read(rstate.Buffer, rstate.Offset, rstate.Count);
                rstate.Offset += nread;
                rstate.Count -= nread;
                if (rstate.Count == 0 || !Decoder.WantMore || nread == 0)
                {
                    _noMoreData = !Decoder.WantMore && nread == 0;
                    ares.Count = rstate.InitialCount - rstate.Count;
                    ares.Complete();

                    return;
                }

                ares.Offset = 0;
                ares.Count = Math.Min(BufferLength, Decoder.ChunkLeft + 6);
                base.BeginRead(ares.Buffer, ares.Offset, ares.Count, onRead, rstate);
            }
            catch (Exception ex)
            {
                _context.ErrorMessage = ex.Message;
                _context.SendError();

                ares.Complete(ex);
            }
        }

        #endregion

        #region Private Fields

        private const int BufferLength = 8192;
        private readonly HttpListenerContext _context;
        private bool _disposed;
        private bool _noMoreData;

        #endregion

        #region Public Methods

        public override IAsyncResult BeginRead(
            byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().ToString());

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "A negative value.");

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "A negative value.");

            var len = buffer.Length;
            if (offset + count > len)
                throw new ArgumentException(
                    "The sum of 'offset' and 'count' is greater than 'buffer' length.");

            var ares = new HttpStreamAsyncResult(callback, state);
            if (_noMoreData)
            {
                ares.Complete();
                return ares;
            }

            var nread = Decoder.Read(buffer, offset, count);
            offset += nread;
            count -= nread;
            if (count == 0)
            {
                // Got all we wanted, no need to bother the decoder yet.
                ares.Count = nread;
                ares.Complete();

                return ares;
            }

            if (!Decoder.WantMore)
            {
                _noMoreData = nread == 0;
                ares.Count = nread;
                ares.Complete();

                return ares;
            }

            ares.Buffer = new byte[BufferLength];
            ares.Offset = 0;
            ares.Count = BufferLength;

            var rstate = new ReadBufferState(buffer, offset, count, ares);
            rstate.InitialCount += nread;
            base.BeginRead(ares.Buffer, ares.Offset, ares.Count, onRead, rstate);

            return ares;
        }

        public override void Close()
        {
            if (_disposed)
                return;

            _disposed = true;
            base.Close();
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().ToString());

            if (asyncResult == null)
                throw new ArgumentNullException(nameof(asyncResult));

            if (!(asyncResult is HttpStreamAsyncResult ares))
                throw new ArgumentException("A wrong IAsyncResult.", nameof(asyncResult));

            if (!ares.IsCompleted)
                ares.AsyncWaitHandle.WaitOne();

            if (ares.HasException)
                throw new HttpListenerException(400, "I/O operation aborted.");

            return ares.Count;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var ares = BeginRead(buffer, offset, count, null, null);
            return EndRead(ares);
        }

        #endregion
    }
}