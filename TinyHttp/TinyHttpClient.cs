﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tiny.Http
{
    public class TinyHttpClient
    {
        #region Fields
        private readonly HttpClient _httpClient;
        private readonly string _serverAddress;
        private readonly ISerializer _defaultSerializer;
        private readonly IDeserializer _defaultDeserializer;
        private Encoding _encoding;
        #endregion

        #region Logging events
        public event EventHandler<HttpSendingRequestEventArgs> SendingRequest;
        public event EventHandler<HttpReceivedResponseEventArgs> ReceivedResponse;
        public event EventHandler<FailedToGetResponseEventArgs> FailedToGetResponse;
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="TinyHttpClient"/> class.
        /// </summary>
        /// <param name="httpClient">The httpclient used</param>
        /// <param name="serverAddress">The server address.</param>
        public TinyHttpClient(HttpClient httpClient, string serverAddress)
            : this(httpClient, serverAddress, new TinyJsonSerializer(), new TinyJsonDeserializer())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TinyHttpClient"/> class.
        /// </summary>
        /// <param name="httpClient">The httpclient used</param>
        /// <param name="serverAddress">The server address.</param>
        /// /// <param name="defaultSerializer">The serializer used for serialize data</param>
        /// <param name="defaultDeserializer">The deserializer used for deszerialiaze data.</param>
        public TinyHttpClient(HttpClient httpClient, string serverAddress, ISerializer defaultSerializer, IDeserializer defaultDeserializer)
        {
            _serverAddress = serverAddress ?? throw new ArgumentNullException(nameof(serverAddress));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _defaultSerializer = defaultSerializer ?? throw new ArgumentNullException(nameof(defaultSerializer));
            _defaultDeserializer = defaultDeserializer ?? throw new ArgumentNullException(nameof(defaultDeserializer));

            DefaultHeaders = new Dictionary<string, string>();

            if (!_serverAddress.EndsWith("/"))
            {
                _serverAddress += "/";
            }

            _encoding = Encoding.UTF8;
        }

        /// <summary>
        /// Gets the default headers.
        /// </summary>
        /// <value>
        /// The default headers.
        /// </value>
        public Dictionary<string, string> DefaultHeaders
        {
            get; private set;
        }

        public Encoding Encoding
        {
            get
            {
                return _encoding;
            }
            set
            {
                _encoding = value ?? throw new ArgumentNullException(nameof(Encoding));
            }
        }

        public IRequest NewRequest(HttpVerb verb, string route = null)
        {
            return new TinyRequest(verb, route, this);
        }

       internal async Task<TResult> ExecuteAsync<TResult>(
           HttpVerb httpVerb,
           string route,
           Dictionary<string, string> headers,
           Dictionary<string, string> queryParameters,
           IEnumerable<KeyValuePair<string, string>> formsParameters,
           ISerializer serializer,
           IDeserializer deserializer,
           ContentType contentType,
           object data,
           CancellationToken cancellationToken)
        {
            if (deserializer == null)
            {
                deserializer = _defaultDeserializer;
            }

            if (serializer == null)
            {
                serializer = _defaultSerializer;
            }

            using (var content = CreateContent(contentType, serializer, data, formsParameters))
            {
                using (var stream = await InternalExecuteRequestAsync(route, headers, queryParameters, formsParameters, deserializer, httpVerb, content, cancellationToken))
                {
                    if (stream == null)
                    {
                        return default;
                    }

                    return deserializer.Deserialize<TResult>(stream);
                }
            }
        }

        internal async Task ExecuteAsync(
            HttpVerb httpVerb,
            string route,
            Dictionary<string, string> headers,
            Dictionary<string, string> queryParameters,
            IEnumerable<KeyValuePair<string, string>> formsParameters,
            ISerializer serializer,
            IDeserializer deserializer,
            ContentType contentType,
            object data,
            CancellationToken cancellationToken)
        {
            if (deserializer == null)
            {
                deserializer = _defaultDeserializer;
            }

            if (serializer == null)
            {
                serializer = _defaultSerializer;
            }

            using (var content = CreateContent(contentType, serializer, data, formsParameters))
            {
                await InternalExecuteRequestAsync(route, headers, queryParameters, formsParameters, deserializer, httpVerb, content, cancellationToken);
            }
        }

        internal async Task<byte[]> ExecuteByteArrayResultAsync(
           HttpVerb httpVerb,
           string route,
           Dictionary<string, string> headers,
           Dictionary<string, string> queryParameters,
           IEnumerable<KeyValuePair<string, string>> formsParameters,
           ISerializer serializer,
           IDeserializer deserializer,
           ContentType contentType,
           object data,
           CancellationToken cancellationToken)
        {
            if (deserializer == null)
            {
                deserializer = _defaultDeserializer;
            }

            if (serializer == null)
            {
                serializer = _defaultSerializer;
            }

            using (var content = CreateContent(contentType, serializer, data, formsParameters))
            {
                using (var stream = await InternalExecuteRequestAsync(route, headers, queryParameters, formsParameters, deserializer, httpVerb, content, cancellationToken))
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
        }

        internal Task<Stream> ExecuteWithStreamResultAsync(
           HttpVerb httpVerb,
           string route,
           Dictionary<string, string> headers,
           Dictionary<string, string> queryParameters,
           IEnumerable<KeyValuePair<string, string>> formsParameters,
           ISerializer serializer,
           IDeserializer deserializer,
           ContentType contentType,
           object data,
           CancellationToken cancellationToken)
        {
            if (deserializer == null)
            {
                deserializer = _defaultDeserializer;
            }

            if (serializer == null)
            {
                serializer = _defaultSerializer;
            }

            using (var content = CreateContent(contentType, serializer, null, formsParameters))
            {
                return InternalExecuteRequestAsync(route, headers, queryParameters, formsParameters, deserializer, httpVerb, content, cancellationToken);
            }
        }

        private HttpContent CreateContent(
            ContentType contentType,
            ISerializer serializer,
            object data,
            IEnumerable<KeyValuePair<string, string>> formsParameters)
        {
            switch (contentType)
            {
                case ContentType.Stream:
                case ContentType.String:

                    if (data == null)
                    {
                        return null;
                    }

                    var content = new StringContent(serializer.Serialize(data, _encoding), _encoding);
                    if (_defaultSerializer.HasMediaType)
                    {
                        content.Headers.ContentType = new MediaTypeHeaderValue(_defaultSerializer.MediaType);
                    }

                    return content;
                case ContentType.Forms:
                    return new FormUrlEncodedContent(formsParameters);
                case ContentType.ByteArray:
                    if (data == null)
                    {
                        return null;
                    }

                    var contentArray = new ByteArrayContent(data as byte[]);
                    contentArray.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    return contentArray;
                default:
                    throw new NotImplementedException();
            }
        }

        private async Task<Stream> InternalExecuteRequestAsync(
            string route,
               Dictionary<string, string> headers,
               Dictionary<string, string> queryParameters,
               IEnumerable<KeyValuePair<string, string>> formsParameters,
               IDeserializer deserializer,
               HttpVerb httpVerb,
               HttpContent content,
               CancellationToken cancellationToken)
        {
            var requestUri = BuildRequestUri(route, queryParameters);
            HttpResponseMessage response = await SendRequestAsync(ConvertToHttpMethod(httpVerb), requestUri, content, deserializer, cancellationToken);
            var stream = await ReadResponseAsync(response, cancellationToken);
            if (stream == null || stream.CanRead == false)
            {
                return null;
            }

            return stream;
        }

        private Uri BuildRequestUri(string route, Dictionary<string, string> queryParameters)
        {
            var stringBuilder = new StringBuilder(string.Concat(_serverAddress, route));

            if (queryParameters.Any())
            {
                var last = queryParameters.Last();
                stringBuilder.Append("?");
                for (int i = 0; i < queryParameters.Count; i++)
                {
                    var item = queryParameters.ElementAt(i);
                    var separator = i == queryParameters.Count - 1 ? string.Empty : "&";
                    stringBuilder.Append($"{item.Key}={item.Value}{separator}");
                }
            }

            return new Uri(stringBuilder.ToString());
        }

        private async Task<HttpResponseMessage> SendRequestAsync(HttpMethod httpMethod, Uri uri, HttpContent content, IDeserializer deserializer, CancellationToken cancellationToken)
        {
            var requestId = Guid.NewGuid().ToString();
            Stopwatch sw = new Stopwatch();
            try
            {
                using (var request = new HttpRequestMessage(httpMethod, uri))
                {
                    if (deserializer.HasMediaType)
                    {
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(deserializer.MediaType));
                    }

                    // TODO : add something to customize that stuff
                    request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(CultureInfo.CurrentCulture.TwoLetterISOLanguageName));
                    request.Headers.AcceptCharset.Add(new StringWithQualityHeaderValue("utf-8"));
                    foreach (var item in DefaultHeaders)
                    {
                        request.Headers.Add(item.Key, item.Value);
                    }

                    if (content != null)
                    {
                        request.Content = content;
                    }

                    OnSendingRequest(requestId, uri, httpMethod);
                    sw.Start();
                    var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
                    sw.Stop();
                    OnReceivedResponse(requestId, uri, httpMethod, response, sw.Elapsed);
                    return response;
                }
            }
            catch (Exception ex)
            {
                sw.Stop();

                OnFailedToReceiveResponse(requestId, uri, httpMethod, ex, sw.Elapsed);

                throw new ConnectionException(
                   "Failed to get a response from server",
                   uri.AbsoluteUri,
                   httpMethod.Method,
                   ex);
            }
        }

        private HttpMethod ConvertToHttpMethod(HttpVerb httpVerb)
        {
            switch (httpVerb)
            {
                case HttpVerb.Get:
                    return HttpMethod.Get;
                case HttpVerb.Post:
                    return HttpMethod.Post;
                case HttpVerb.Put:
                    return HttpMethod.Put;
                case HttpVerb.Delete:
                    return HttpMethod.Delete;
                case HttpVerb.Head:
                    return HttpMethod.Head;
                case HttpVerb.Patch:
                    return new HttpMethod("PATCH");
                case HttpVerb.Copy:
                    return new HttpMethod("COPY");
                default:
                    throw new NotImplementedException();
            }
        }

        #region Read response
        private async Task<Stream> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            Stream stream = null;
            string content = null;
            try
            {
                stream = await response.Content.ReadAsStreamAsync();

                if (response.IsSuccessStatusCode)
                {
                    return stream;
                }
                else
                {
                    content = await StreamToStringAsync(stream);
                }

                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                var newEx = new HttpException(
                    $"URL : {response.RequestMessage.RequestUri.ToString()}",
                    response.RequestMessage.Headers,
                    response.ReasonPhrase,
                    response.RequestMessage.RequestUri.ToString(),
                    response.RequestMessage.Method.ToString(),
                    content,
                    response.StatusCode,
                    ex);

                throw newEx;
            }

            return stream;
        }

        private static async Task<string> StreamToStringAsync(Stream stream)
        {
            string content = null;

            if (stream != null)
            {
                using (var sr = new StreamReader(stream))
                {
                    content = await sr.ReadToEndAsync();
                }
            }

            return content;
        }
        #endregion

        #region Events invoker
        private void OnSendingRequest(string requestId, Uri url, HttpMethod httpMethod)
        {
            try
            {
                SendingRequest?.Invoke(this, new HttpSendingRequestEventArgs(requestId, url.ToString(), httpMethod.Method));
            }
            catch
            {
                // ignored
            }
        }

        private void OnReceivedResponse(string requestId, Uri uri, HttpMethod httpMethod, HttpResponseMessage response, TimeSpan elapsedTime)
        {
            try
            {
                ReceivedResponse?.Invoke(this, new HttpReceivedResponseEventArgs(requestId, uri.AbsoluteUri, httpMethod.Method, response.StatusCode, response.ReasonPhrase, elapsedTime));
            }
            catch
            {
                // ignored
            }
        }

        private void OnFailedToReceiveResponse(string requestId, Uri uri, HttpMethod httpMethod, Exception exception, TimeSpan elapsedTime)
        {
            try
            {
                FailedToGetResponse?.Invoke(this, new FailedToGetResponseEventArgs(requestId, uri.AbsoluteUri, httpMethod.Method, exception, elapsedTime));
            }
            catch
            {
                // ignored
            }
        }
        #endregion
    }
}