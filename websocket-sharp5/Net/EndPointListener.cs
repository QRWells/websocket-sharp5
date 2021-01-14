// ReSharper disable CommentTypo

#region License

/*
 * EndPointListener.cs
 *
 * This code is derived from EndPointListener.cs (System.Net) of Mono
 * (http://www.mono-project.com).
 *
 * The MIT License
 *
 * Copyright (c) 2005 Novell, Inc. (http://www.novell.com)
 * Copyright (c) 2012-2020 sta.blockhead
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

#region Contributors

/*
 * Contributors:
 * - Liryna <liryna.stark@gmail.com>
 * - Nicholas Devenish
 */

#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

// ReSharper disable IdentifierTypo

namespace WebSocketSharp.Net
{
    internal sealed class EndPointListener
    {
        #region Static Constructor

        static EndPointListener()
        {
            _defaultCertFolderPath = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData
            );
        }

        #endregion

        #region Internal Constructors

        internal EndPointListener(
            IPEndPoint endpoint,
            bool secure,
            string certificateFolderPath,
            ServerSslConfiguration sslConfig,
            bool reuseAddress
        )
        {
            _endpoint = endpoint;

            if (secure)
            {
                var cert = getCertificate(
                    endpoint.Port,
                    certificateFolderPath,
                    sslConfig.ServerCertificate
                );

                if (cert == null)
                {
                    var msg = "No server certificate could be found.";

                    throw new ArgumentException(msg);
                }

                IsSecure = true;
                SslConfiguration = new ServerSslConfiguration(sslConfig) {ServerCertificate = cert};
            }

            _prefixes = new List<HttpListenerPrefix>();
            _unregistered = new List<HttpConnection>();
            _unregisteredSync = ((ICollection) _unregistered).SyncRoot;

            _socket = new Socket(
                endpoint.Address.AddressFamily,
                SocketType.Stream,
                ProtocolType.Tcp
            );

            if (reuseAddress)
                _socket.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true
                );

            _socket.Bind(endpoint);
            _socket.Listen(500);
            _socket.BeginAccept(onAccept, this);
        }

        #endregion

        #region Private Fields

        private List<HttpListenerPrefix> _all; // host == '+'
        private static readonly string _defaultCertFolderPath;
        private readonly IPEndPoint _endpoint;
        private List<HttpListenerPrefix> _prefixes;
        private readonly Socket _socket;
        private List<HttpListenerPrefix> _unhandled; // host == '*'
        private readonly List<HttpConnection> _unregistered;
        private readonly object _unregisteredSync;

        #endregion

        #region Public Properties

        public IPAddress Address => _endpoint.Address;

        public bool IsSecure { get; }

        public int Port => _endpoint.Port;

        public ServerSslConfiguration SslConfiguration { get; }

        #endregion

        #region Private Methods

        private static void addSpecial(
            ICollection<HttpListenerPrefix> prefixes, HttpListenerPrefix prefix
        )
        {
            var path = prefix.Path;

            if (prefixes.Any(pref => pref.Path == path))
            {
                const string msg = "The prefix is already in use.";

                throw new HttpListenerException(87, msg);
            }

            prefixes.Add(prefix);
        }

        private void clearConnections()
        {
            HttpConnection[] conns;

            int cnt;

            lock (_unregisteredSync)
            {
                cnt = _unregistered.Count;

                if (cnt == 0)
                    return;

                conns = new HttpConnection[cnt];

                _unregistered.CopyTo(conns, 0);
                _unregistered.Clear();
            }

            for (var i = cnt - 1; i >= 0; i--)
                conns[i].Close(true);
        }

        private static RSACryptoServiceProvider CreateRsaFromFile(string path)
        {
            var rsa = new RSACryptoServiceProvider();

            var key = File.ReadAllBytes(path);
            rsa.ImportCspBlob(key);

            return rsa;
        }

        private static X509Certificate2 getCertificate(
            int port, string folderPath, X509Certificate2 defaultCertificate
        )
        {
            if (string.IsNullOrEmpty(folderPath))
                folderPath = _defaultCertFolderPath;

            try
            {
                var cer = Path.Combine(folderPath, $"{port}.cer");
                var key = Path.Combine(folderPath, $"{port}.key");

                if (File.Exists(cer) && File.Exists(key))
                {
                    var cert = new X509Certificate2(cer) {PrivateKey = CreateRsaFromFile(key)};

                    return cert;
                }
            }
            catch
            {
                // ignored
            }

            return defaultCertificate;
        }

        private void leaveIfNoPrefix()
        {
            if (_prefixes.Count > 0)
                return;

            var prefs = _unhandled;

            if (prefs != null && prefs.Count > 0)
                return;

            prefs = _all;

            if (prefs != null && prefs.Count > 0)
                return;

            Close();
        }

        private static void onAccept(IAsyncResult asyncResult)
        {
            var lsnr = (EndPointListener) asyncResult.AsyncState;

            Socket sock = null;

            try
            {
                if (lsnr != null) sock = lsnr._socket.EndAccept(asyncResult);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception)
            {
                // TODO: Logging.
            }

            try
            {
                lsnr?._socket.BeginAccept(onAccept, lsnr);
            }
            catch (Exception)
            {
                // TODO: Logging.

                sock?.Close();

                return;
            }

            if (sock == null)
                return;

            processAccepted(sock, lsnr);
        }

        private static void processAccepted(
            Socket socket, EndPointListener listener
        )
        {
            HttpConnection conn;

            try
            {
                conn = new HttpConnection(socket, listener);
            }
            catch (Exception)
            {
                // TODO: Logging.

                socket.Close();

                return;
            }

            lock (listener._unregisteredSync)
            {
                listener._unregistered.Add(conn);
            }

            conn.BeginReadRequest();
        }

        private static bool removeSpecial(
            List<HttpListenerPrefix> prefixes, HttpListenerPrefix prefix
        )
        {
            var path = prefix.Path;
            var cnt = prefixes.Count;

            for (var i = 0; i < cnt; i++)
                if (prefixes[i].Path == path)
                {
                    prefixes.RemoveAt(i);

                    return true;
                }

            return false;
        }

        private static HttpListener searchHttpListenerFromSpecial(
            string path, IReadOnlyCollection<HttpListenerPrefix> prefixes
        )
        {
            if (prefixes == null)
                return null;

            HttpListener ret = null;

            var bestLen = -1;

            foreach (var pref in prefixes)
            {
                var prefPath = pref.Path;
                var len = prefPath.Length;

                if (len < bestLen)
                    continue;

                if (!path.StartsWith(prefPath, StringComparison.Ordinal)) continue;
                bestLen = len;
                ret = pref.Listener;
            }

            return ret;
        }

        #endregion

        #region Internal Methods

        internal static bool CertificateExists(int port, string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                folderPath = _defaultCertFolderPath;

            var cer = Path.Combine(folderPath, $"{port}.cer");
            var key = Path.Combine(folderPath, $"{port}.key");

            return File.Exists(cer) && File.Exists(key);
        }

        internal void RemoveConnection(HttpConnection connection)
        {
            lock (_unregisteredSync)
            {
                _unregistered.Remove(connection);
            }
        }

        internal bool TrySearchHttpListener(Uri uri, out HttpListener listener)
        {
            listener = null;

            if (uri == null)
                return false;

            var host = uri.Host;
            var dns = Uri.CheckHostName(host) == UriHostNameType.Dns;
            var port = uri.Port.ToString();
            var path = HttpUtility.UrlDecode(uri.AbsolutePath);

            if (path[^1] != '/')
                path += "/";

            if (host.Length > 0)
            {
                var prefs = _prefixes;
                var bestLen = -1;

                foreach (var pref in prefs)
                {
                    if (dns)
                    {
                        var prefHost = pref.Host;
                        var prefDns = Uri.CheckHostName(prefHost) == UriHostNameType.Dns;

                        if (prefDns)
                            if (prefHost != host)
                                continue;
                    }

                    if (pref.Port != port)
                        continue;

                    var prefPath = pref.Path;
                    var len = prefPath.Length;

                    if (len < bestLen)
                        continue;

                    if (!path.StartsWith(prefPath, StringComparison.Ordinal)) continue;
                    bestLen = len;
                    listener = pref.Listener;
                }

                if (bestLen != -1)
                    return true;
            }

            listener = searchHttpListenerFromSpecial(path, _unhandled);

            if (listener != null)
                return true;

            listener = searchHttpListenerFromSpecial(path, _all);

            return listener != null;
        }

        #endregion

        #region Public Methods

        public void AddPrefix(HttpListenerPrefix prefix)
        {
            List<HttpListenerPrefix> current, future;

            switch (prefix.Host)
            {
                case "*":
                {
                    do
                    {
                        current = _unhandled;
                        future = current != null
                            ? new List<HttpListenerPrefix>(current)
                            : new List<HttpListenerPrefix>();

                        addSpecial(future, prefix);
                    } while (
                        Interlocked.CompareExchange(ref _unhandled, future, current) != current
                    );

                    return;
                }
                case "+":
                {
                    do
                    {
                        current = _all;
                        future = current != null
                            ? new List<HttpListenerPrefix>(current)
                            : new List<HttpListenerPrefix>();

                        addSpecial(future, prefix);
                    } while (
                        Interlocked.CompareExchange(ref _all, future, current) != current
                    );

                    return;
                }
            }

            do
            {
                current = _prefixes;
                if (current != null)
                {
                    var idx = current.IndexOf(prefix);

                    if (idx > -1)
                    {
                        if (current[idx].Listener != prefix.Listener)
                        {
                            var msg = $"There is another listener for {prefix}.";

                            throw new HttpListenerException(87, msg);
                        }

                        return;
                    }
                }

                future = new List<HttpListenerPrefix>(current) {prefix};
            } while (
                Interlocked.CompareExchange(ref _prefixes, future, current) != current
            );
        }

        public void Close()
        {
            _socket.Close();

            clearConnections();
            EndPointManager.RemoveEndPoint(_endpoint);
        }

        public void RemovePrefix(HttpListenerPrefix prefix)
        {
            List<HttpListenerPrefix> current, future;

            if (prefix.Host == "*")
            {
                do
                {
                    current = _unhandled;

                    if (current == null)
                        break;

                    future = new List<HttpListenerPrefix>(current);

                    if (!removeSpecial(future, prefix))
                        break;
                } while (
                    Interlocked.CompareExchange(ref _unhandled, future, current) != current
                );

                leaveIfNoPrefix();

                return;
            }

            if (prefix.Host == "+")
            {
                do
                {
                    current = _all;

                    if (current == null)
                        break;

                    future = new List<HttpListenerPrefix>(current);

                    if (!removeSpecial(future, prefix))
                        break;
                } while (
                    Interlocked.CompareExchange(ref _all, future, current) != current
                );

                leaveIfNoPrefix();

                return;
            }

            do
            {
                current = _prefixes;

                if (current != null && !current.Contains(prefix))
                    break;

                future = new List<HttpListenerPrefix>(current);
                future.Remove(prefix);
            } while (
                Interlocked.CompareExchange(ref _prefixes, future, current) != current
            );

            leaveIfNoPrefix();
        }

        #endregion
    }
}