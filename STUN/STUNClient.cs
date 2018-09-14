using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections;
using STUN.Attributes;
using System.IO;

namespace STUN
{
    /// <summary>
    /// Implements a RFC3489 STUN client.
    /// </summary>
    public static class STUNClient
    {
        /// <summary>
        /// Period of time in miliseconds to wait for server response.
        /// </summary>
        public static int ReceiveTimeout = 2000;

        /// <param name="server">Server address</param>
        /// <param name="queryType">Query type</param>
        /// <param name="closeSocket">
        /// Set to true if created socket should closed after the query
        /// else <see cref="STUNQueryResult.Socket"/> will leave open and can be used.
        /// </param>
        public static Task<STUNQueryResult> QueryAsync(IPEndPoint server, STUNQueryType queryType, bool closeSocket)
        {
            return Task.Run(() => Query(server, queryType, closeSocket));
        }

        /// <param name="socket">A UDP <see cref="Socket"/> that will use for query. You can also use <see cref="UdpClient.Client"/></param>
        /// <param name="server">Server address</param>
        /// <param name="queryType">Query type</param>
        public static Task<STUNQueryResult> QueryAsync(Socket socket, IPEndPoint server, STUNQueryType queryType)
        {
            return Task.Run(() => Query(socket, server, queryType));
        }

        /// <param name="server">Server address</param>
        /// <param name="queryType">Query type</param>
        /// <param name="closeSocket">
        /// Set to true if created socket should closed after the query
        /// else <see cref="STUNQueryResult.Socket"/> will leave open and can be used.
        /// </param>
        public static STUNQueryResult Query(IPEndPoint server, STUNQueryType queryType, bool closeSocket)
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPEndPoint bindEndPoint = new IPEndPoint(IPAddress.Any, 0);
            socket.Bind(bindEndPoint);

            var result = Query(socket, server, queryType);

            if (closeSocket)
            {
                socket.Dispose();
                result.Socket = null;
            }

            return result;
        }

        /// <param name="socket">A UDP <see cref="Socket"/> that will use for query. You can also use <see cref="UdpClient.Client"/></param>
        /// <param name="server">Server address</param>
        /// <param name="queryType">Query type</param>
        public static STUNQueryResult Query(Socket socket, IPEndPoint server, STUNQueryType queryType)
        {
            var result = new STUNQueryResult(); // the query result

            var transID = STUNMessage.GenerateTransactionID(); // get a random trans id
            var message = new STUNMessage(STUNMessageTypes.BindingRequest, transID); // create a bind request

            result.Socket = socket; 
            result.ServerEndPoint = server;
            result.NATType = STUNNATType.Unspecified;
            result.QueryType = queryType;

            // send the request to server
            socket.SendTo(message.GetBytes(), server);
            // we set result local endpoint after calling SendTo,
            // because if socket is unbound, the system will bind it after SendTo call.
            result.LocalEndPoint = socket.LocalEndPoint as IPEndPoint;

            // wait for response
            var responseBuffer = Receive(socket);

            // didn't receive anything
            if (responseBuffer == null)
            {
                result.QueryError = STUNQueryError.Timedout;
                return result;
            }

            // try to parse message
            if (!message.TryParse(responseBuffer))
            {
                result.QueryError = STUNQueryError.BadResponse;
                return result;
            }

            // check trans id
            if (!ByteArrayCompare(message.TransactionID, transID))
            {
                result.QueryError = STUNQueryError.BadTransactionID;
                return result;
            }

            // finds error-code attribute, used in case of binding error
            var errorAttr = message.Attributes.FirstOrDefault(p => p is STUNErrorCodeAttribute)
                                                                        as STUNErrorCodeAttribute;

            // if server responsed our request with error
            if (message.MessageType == STUNMessageTypes.BindingErrorResponse)
            {
                if (errorAttr == null)
                {
                    // we count a binding error without error-code attribute as bad response (no?)
                    result.QueryError = STUNQueryError.BadResponse;
                    return result;
                }

                result.QueryError = STUNQueryError.ServerError;
                result.ServerError = errorAttr.Error;
                result.ServerErrorPhrase = errorAttr.Phrase;
                return result;
            }

            // return if receive something else binding response
            if (message.MessageType != STUNMessageTypes.BindingResponse)
            {
                result.QueryError = STUNQueryError.BadResponse;
                return result;
            }

            // not used for now.
            var changedAddr = message.Attributes.FirstOrDefault(p => p is STUNChangedAddressAttribute) as STUNChangedAddressAttribute;

            // find mapped address attribue in message
            // this attribue should present
            var mappedAddressAttr = message.Attributes.FirstOrDefault(p => p is STUNMappedAddressAttribute) 
                                                                                as STUNMappedAddressAttribute;
            if (mappedAddressAttr == null)
            {
                result.QueryError = STUNQueryError.BadResponse;
                return result;
            }
            else
            {
                result.PublicEndPoint = mappedAddressAttr.EndPoint;
            }

            // stop querying and return the public ip if user just wanted to know public ip
            if (queryType == STUNQueryType.PublicIP)
            {
                result.QueryError = STUNQueryError.Success;
                return result;
            }

            // if our local ip and port equals to mapped address
            if (mappedAddressAttr.EndPoint.Equals(socket.LocalEndPoint))
            {
                // we send to a binding request again but with change-request attribute
                // that tells to server to response us with different endpoint
                message = new STUNMessage(STUNMessageTypes.BindingRequest, transID);
                message.Attributes.Add(new STUNChangeRequestAttribute(true, true));

                socket.SendTo(message.GetBytes(), server);
                responseBuffer = Receive(socket);

                // if we didnt receive a response
                if (responseBuffer == null)
                {
                    result.QueryError = STUNQueryError.Success;
                    result.NATType = STUNNATType.SymmetricUDPFirewall;
                    return result;
                }

                if(!message.TryParse(responseBuffer))
                {
                    result.QueryError = STUNQueryError.BadResponse;
                    return result;
                }

                if (!ByteArrayCompare(message.TransactionID, transID))
                {
                    result.QueryError = STUNQueryError.BadTransactionID;
                    return result;
                }

                if (message.MessageType == STUNMessageTypes.BindingResponse)
                {
                    result.QueryError = STUNQueryError.Success;
                    result.NATType = STUNNATType.OpenInternet;
                    return result;
                }
                
                if (message.MessageType == STUNMessageTypes.BindingErrorResponse)
                {
                    errorAttr = message.Attributes.FirstOrDefault(p => p is STUNErrorCodeAttribute) as STUNErrorCodeAttribute;

                    if (errorAttr == null)
                    {
                        result.QueryError = STUNQueryError.BadResponse;
                        return result;
                    }

                    result.QueryError = STUNQueryError.ServerError;
                    result.ServerError = errorAttr.Error;
                    result.ServerErrorPhrase = errorAttr.Phrase;
                    return result;
                }

                // the message type is wrong
                result.QueryError = STUNQueryError.BadResponse;
                return result;
            }

            message = new STUNMessage(STUNMessageTypes.BindingRequest, transID);
            message.Attributes.Add(new STUNChangeRequestAttribute(true, true));

            var testmsg = new STUNMessage(STUNMessageTypes.BindingRequest, null);
            testmsg.Parse(message.GetBytes());
            
            socket.SendTo(message.GetBytes(), server);

            responseBuffer = Receive(socket);
            
            if (responseBuffer != null)
            {
                if (!message.TryParse(responseBuffer))
                {
                    result.QueryError = STUNQueryError.BadResponse;
                    return result;
                }

                if (!ByteArrayCompare(message.TransactionID, transID))
                {
                    result.QueryError = STUNQueryError.BadTransactionID;
                    return result;
                }

                if (message.MessageType == STUNMessageTypes.BindingResponse)
                {
                    result.QueryError = STUNQueryError.Success;
                    result.NATType = STUNNATType.FullCone;
                    return result;
                }

                if (message.MessageType == STUNMessageTypes.BindingErrorResponse)
                {
                    errorAttr = message.Attributes.FirstOrDefault(p => p is STUNErrorCodeAttribute) as STUNErrorCodeAttribute;

                    if (errorAttr == null)
                    {
                        result.QueryError = STUNQueryError.BadResponse;
                        return result;
                    }

                    result.QueryError = STUNQueryError.ServerError;
                    result.ServerError = errorAttr.Error;
                    result.ServerErrorPhrase = errorAttr.Phrase;
                    return result;
                }

                result.QueryError = STUNQueryError.BadResponse;
                return result;
            }

            // if user only wanted to know the NAT is open or not
            if (queryType == STUNQueryType.OpenNAT)
            {
                result.QueryError = STUNQueryError.Success;
                result.NATType = STUNNATType.Unspecified;
                return result;
            }

            // we now need changed-address attribute
            // because we send our request to this address instead of the first server address
            if (changedAddr == null)
            {
                result.QueryError = STUNQueryError.BadResponse;
                return result;
            }
            else
            {
                server = changedAddr.EndPoint;
            }

            message = new STUNMessage(STUNMessageTypes.BindingRequest, transID);
            socket.SendTo(message.GetBytes(), server);

            responseBuffer = Receive(socket);

            if (responseBuffer == null)
            {
                result.QueryError = STUNQueryError.Timedout;
                return result;
            }

            if (!message.TryParse(responseBuffer))
            {
                result.QueryError = STUNQueryError.BadResponse;
                return result;
            }

            if (!ByteArrayCompare(message.TransactionID, transID))
            {
                result.QueryError = STUNQueryError.BadTransactionID;
                return result;
            }

            errorAttr = message.Attributes.FirstOrDefault(p => p is STUNErrorCodeAttribute) as STUNErrorCodeAttribute;

            if (message.MessageType == STUNMessageTypes.BindingErrorResponse)
            {
                if (errorAttr == null)
                {
                    result.QueryError = STUNQueryError.BadResponse;
                    return result;
                }

                result.QueryError = STUNQueryError.ServerError;
                result.ServerError = errorAttr.Error;
                result.ServerErrorPhrase = errorAttr.Phrase;
                return result;
            }

            if (message.MessageType != STUNMessageTypes.BindingResponse)
            {
                result.QueryError = STUNQueryError.BadResponse;
                return result;
            }

            mappedAddressAttr = message.Attributes.FirstOrDefault(p => p is STUNMappedAddressAttribute) 
                                                                               as STUNMappedAddressAttribute;

            if (mappedAddressAttr == null)
            {
                result.QueryError = STUNQueryError.BadResponse;
                return result;
            }

            if (!mappedAddressAttr.EndPoint.Equals(result.PublicEndPoint))
            {
                result.QueryError = STUNQueryError.Success;
                result.NATType = STUNNATType.Symmetric;
                result.PublicEndPoint = null;
                return result;
            }

            message = new STUNMessage(STUNMessageTypes.BindingRequest, transID);
            message.Attributes.Add(new STUNChangeRequestAttribute(false, true)); // change port but not ip

            socket.SendTo(message.GetBytes(), server);

            responseBuffer = Receive(socket);
            
            if (responseBuffer == null)
            {
                result.QueryError = STUNQueryError.Success;
                result.NATType = STUNNATType.PortRestricted;
                return result;
            }

            if (!message.TryParse(responseBuffer))
            {
                result.QueryError = STUNQueryError.Timedout;
                return result;
            }

            if (ByteArrayCompare(message.TransactionID, transID))
            {
                result.QueryError = STUNQueryError.BadTransactionID;
                return result;
            }

            errorAttr = message.Attributes.FirstOrDefault(p => p is STUNErrorCodeAttribute)
                                                                                as STUNErrorCodeAttribute;

            if (message.MessageType == STUNMessageTypes.BindingErrorResponse)
            {
                if (errorAttr == null)
                {
                    result.QueryError = STUNQueryError.BadResponse;
                    return result;
                }

                result.QueryError = STUNQueryError.ServerError;
                result.ServerError = errorAttr.Error;
                result.ServerErrorPhrase = errorAttr.Phrase;
                return result;
            }

            if (message.MessageType != STUNMessageTypes.BindingResponse)
            {
                result.QueryError = STUNQueryError.BadResponse;
                return result;
            }

            result.QueryError = STUNQueryError.Success;
            result.NATType = STUNNATType.Restricted;
            return result;
        }

        static byte[] Receive(Socket socket)
        {
            return Receive(socket, ReceiveTimeout);
        }

        static byte[] Receive(Socket socket, int timeout)
        {
            if (!socket.Poll(timeout * 1000, SelectMode.SelectRead))
            {
                return null;
            }

            EndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
            
            byte[] buffer = new byte[1024 * 2];
            int bytesRead = 0;

            bytesRead = socket.ReceiveFrom(buffer, ref endPoint);

            return buffer.Take(bytesRead).ToArray();
        }

        public static bool TryParseHostAndPort(string hostAndPort, out IPEndPoint endPoint)
        {
            if (string.IsNullOrWhiteSpace(hostAndPort))
            {
                endPoint = null;
                return false;
            }

            var split = hostAndPort.Split(':');

            if (split.Length != 2)
            {
                endPoint = null;
                return false;
            }

            if (!ushort.TryParse(split[1], out ushort port))
            {
                endPoint = null;
                return false;
            }

            if (!IPAddress.TryParse(split[0], out IPAddress address))
            {
                try
                {
#if NETSTANDARD1_3
                    address = Dns.GetHostEntryAsync(split[0]).GetAwaiter().GetResult().AddressList.First();
#else
                    address = Dns.GetHostEntry(split[0]).AddressList.First();
#endif
                }
                catch
                {
                    endPoint = null;
                    return false;
                }
            }

            endPoint = new IPEndPoint(address, port);
            return true;
        }

        static bool ByteArrayCompare(byte[] b1, byte[] b2)
        {
            if (b1 == b2)
                return true;

            if (b1.Length != b2.Length)
                return false;

            return b1.SequenceEqual(b2);
        }
    }
}
