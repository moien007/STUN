using System;
using System.Net;
using STUN;

namespace TestApp
{
    class Program
    {
        static void Main(string[] args)
        {
            if (!STUNClient.TryParseHostAndPort("stun3.l.google.com:19302", out IPEndPoint stunEndPoint))
                throw new Exception("Failed to resolve STUN server address");

            var queryResult = STUNClient.Query(stunEndPoint, STUNQueryType.ExactNAT, false);
            
            if (queryResult.QueryError != STUNQueryError.Success)
                throw new Exception("Query Error: " + queryResult.QueryError.ToString());

            Console.WriteLine("PublicEndPoint: {0}", queryResult.PublicEndPoint);
            Console.WriteLine("LocalEndPoint: {0}", queryResult.LocalEndPoint);
            Console.WriteLine("NAT Type: {0}", queryResult.NATType);
            Console.ReadKey();
        }
    }
}
