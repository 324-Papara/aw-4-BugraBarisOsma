using Microsoft.IO;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Para.Base.Log;

namespace Para.Api.Middleware
{
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate next;
        private readonly RecyclableMemoryStreamManager recyclableMemoryStreamManager;
        private readonly Action<RequestProfilerModel> requestResponseHandler;
        private const int ReadChunkBufferLength = 4096;

        public RequestLoggingMiddleware(RequestDelegate next, Action<RequestProfilerModel> requestResponseHandler)
        {
            this.next = next;
            this.requestResponseHandler = requestResponseHandler;
            this.recyclableMemoryStreamManager = new RecyclableMemoryStreamManager();
        }

        public async Task Invoke(HttpContext context)
        {
            Log.Information("LogRequestLoggingMiddleware.Invoke");

            var model = new RequestProfilerModel
            {
                RequestTime = DateTimeOffset.UtcNow,
                Context = context,
                Request = await FormatRequest(context)
            };

            Stream originalBody = context.Response.Body;

            using (var newResponseBody = recyclableMemoryStreamManager.GetStream())
            {
                context.Response.Body = newResponseBody;

                try
                {
                    await next(context);
                }
                finally
                {
                    newResponseBody.Seek(0, SeekOrigin.Begin);
                    await newResponseBody.CopyToAsync(originalBody);
                    context.Response.Body = originalBody;
                }

                newResponseBody.Seek(0, SeekOrigin.Begin);
                model.Response = FormatResponse(context, newResponseBody);
                model.ResponseTime = DateTimeOffset.UtcNow;

                requestResponseHandler(model);
            }
        }

        private string FormatResponse(HttpContext context, MemoryStream newResponseBody)
        {
            HttpRequest request = context.Request;
            HttpResponse response = context.Response;

            string responseBody = ReadStreamInChunks(newResponseBody);

            return $"Http Response Information: {Environment.NewLine}" +
                   $"Schema: {request.Scheme} {Environment.NewLine}" +
                   $"Host: {request.Host} {Environment.NewLine}" +
                   $"Path: {request.Path} {Environment.NewLine}" +
                   $"QueryString: {request.QueryString} {Environment.NewLine}" +
                   $"StatusCode: {response.StatusCode} {Environment.NewLine}" +
                   $"Response Body: {responseBody}";
        }

        private async Task<string> FormatRequest(HttpContext context)
        {
            HttpRequest request = context.Request;

            string requestBody = await GetRequestBody(request);

            return $"Http Request Information: {Environment.NewLine}" +
                   $"Schema: {request.Scheme} {Environment.NewLine}" +
                   $"Host: {request.Host} {Environment.NewLine}" +
                   $"Path: {request.Path} {Environment.NewLine}" +
                   $"QueryString: {request.QueryString} {Environment.NewLine}" +
                   $"Request Body: {requestBody}";
        }

        public async Task<string> GetRequestBody(HttpRequest request)
        {
            request.EnableBuffering();
            using (var requestStream = recyclableMemoryStreamManager.GetStream())
            {
                await request.Body.CopyToAsync(requestStream);
                request.Body.Seek(0, SeekOrigin.Begin);
                return ReadStreamInChunks(requestStream);
            }
        }

        private static string ReadStreamInChunks(Stream stream)
        {
            stream.Seek(0, SeekOrigin.Begin);
            using (var textWriter = new StringWriter())
            using (var reader = new StreamReader(stream))
            {
                var buffer = new char[ReadChunkBufferLength];
                int bytesRead;

                while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    textWriter.Write(buffer, 0, bytesRead);
                }

                return textWriter.ToString();
            }
        }
    }
}
