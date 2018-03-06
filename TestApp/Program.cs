using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using STUN;

namespace TestApp
{
    class Program
    {
        static void Main(string[] args)
        {
            const string HOSTNAME = @"stun2.l.google.com";
            const int HOSTPORT = 19302;

            var serverIp = Dns.GetHostEntry(HOSTNAME).AddressList.First();
            var serverEndPoint = new IPEndPoint(serverIp, HOSTPORT);

            Console.WriteLine("Querying public IP address...");
            var queryResult = STUNClient.Query(serverEndPoint, STUNQueryType.ExactNAT, true);

            if (queryResult.QueryError == STUNQueryError.Success)
            {
                Console.WriteLine("Query success, Public IP: {0}, NAT Type: {1}", queryResult.PublicEndPoint, queryResult.NATType);
            }
            else
            {
                Console.WriteLine("Query error: {0}", queryResult.QueryError);
            }

            Console.ReadKey();
        }
    }
}
