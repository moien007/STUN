using System;
using System.Net;
using STUN;
using STUN.Attributes;

namespace TestApp
{
    class Program
    {
        static void Main(string[] args)
        {
            // args[0] - STUN server IP and port, e.g. "stun.schlund.de:3478"
            // args[1] - local endpoint address to bind to, e.g. "10.0.0.2" (if any address "0.0.0.0" provided, then NAT type won't be correctly detected).
            String stunAddresAndPort = args[0];
            String localAddress = args[1];

            if (!STUNUtils.TryParseHostAndPort(stunAddresAndPort, out IPEndPoint stunEndPoint))
                throw new Exception("Failed to resolve STUN server address");

            STUNClient.ReceiveTimeout = 500;
            var queryResult = STUNClient.Query(stunEndPoint, STUNQueryType.ExactNAT, true, NATTypeDetectionRFC.Rfc5780, localAddress);
            
            if (queryResult.QueryError != STUNQueryError.Success)
                throw new Exception("Query Error: " + queryResult.QueryError.ToString());

            Console.WriteLine("PublicEndPoint: {0}", queryResult.PublicEndPoint);
            Console.WriteLine("LocalEndPoint: {0}", queryResult.LocalEndPoint);
            Console.WriteLine("NAT Type: {0}", queryResult.NATType);
        }
    }
}
