using System;
using System.Net;
using System.Net.Http;

namespace RiotApiLib
{
    internal static class HttpClientHelper
    {
        internal static HttpClient CreateClient()
        {
            return new HttpClient(new HttpClientHandler
            {
                UseCookies = false,
                UseProxy = false,
                Proxy = null,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
                CheckCertificateRevocationList = false
            });
        }
    }
}