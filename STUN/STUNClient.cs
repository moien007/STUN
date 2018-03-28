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
    public enum STUNNATType
    {
        /// <summary>
        /// Unspecified NAT Type
        /// </summary>
        Unspecified,

        /// <summary>
        /// Open internet. for example virtual servers.
        /// </summary>
        OpenInternet,

        /// <summary>
        /// Full Cone NAT. Good to go.
        /// </summary>
        FullCone,

        /// <summary>
        /// Restricted Cone NAT.
        /// It mean's client can only receive data only IP addresses that it sent a data before.
        /// </summary>
        Restricted,

        /// <summary>
        /// Port-Restricted Cone NAT.
        /// Same as <see cref="Restricted"/> but port is included too.
        /// </summary>
        PortRestricted,

        /// <summary>
        /// Symmetric NAT.
        /// It's means the client pick's a different port for every connection it made.
        /// </summary>
        Symmetric,

        /// <summary>
        /// Same as <see cref="OpenInternet"/> but only received data from addresses that it sent a data before.
        /// </summary>
        SymmetricUDPFirewall,
    }

    public enum STUNQueryError
    {
        /// <summary>
        /// Indicates querying was successful.
        /// </summary>
        Success,

        /// <summary>
        /// Indicates the server responsed with error.
        /// In this case you have check <see cref="STUNQueryResult.ServerError"/> and <see cref="STUNQueryResult.ServerErrorPhrase"/> in query result.
        /// </summary>
        ServerError,

        /// <summary>
        /// Indicates the server responsed a bad\wrong\.. message. This error will returned in many cases.  
        /// </summary>
        BadResponse,

        /// <summary>
        /// Indicates the server responsed a message that contains a different transcation ID 
        /// </summary>
        BadTransactionID,

        /// <summary>
        /// Indicates the server didn't response a request within a time interval
        /// </summary>
        Timedout,
    }

    public enum STUNQueryType
    {
        /// <summary>
        /// Indicates to client to just query IP address and not NAT type
        /// </summary>
        PublicIP,

        /// <summary>
        /// Indicates to client to stop the querying if NAT type is strict.
        /// If the NAT is strict the NAT type will set too <see cref="STUNNATType.Unspecified"/> 
        /// Else the NAT type will set to one of these types
        /// <see cref="STUNNATType.OpenInternet"/>
        /// <see cref="STUNNATType.SymmetricUDPFirewall"/>
        /// <see cref="STUNNATType.FullCone"/>
        /// </summary>
        OpenNAT,

        /// <summary>
        /// Indicates to client to find the exact NAT type.
        /// </summary>
        ExactNAT,
    }

    /// <summary>
    /// STUN client querying result
    /// </summary>
    public class STUNQueryResult
    {
        /// <summary>
        /// The query type that passed to method
        /// </summary>
        public STUNQueryType QueryType { get; set; }

        /// <summary>
        /// The query result error
        /// </summary>
        public STUNQueryError QueryError { get; set; }

        /// <summary>
        /// Contains the server error code that receive from server.
        /// Presents if <see cref="QueryError"/> set too <see cref="STUNQueryError.ServerError"/>
        /// </summary>
        public STUNErrorCodes ServerError { get; set; }

        /// <summary>
        /// Contains the server error phrase that receive from server.
        /// Presents if <see cref="QueryError"/> set to <see cref="STUNQueryError.ServerError"/>
        /// </summary>
        public string ServerErrorPhrase { get; set; }

        /// <summary>
        /// The socket that used to communicate with STUN server
        /// </summary>
        public Socket Socket { get; set; }

        /// <summary>
        /// Contains the server address
        /// </summary>
        public IPEndPoint ServerEndPoint { get; set; }

        /// <summary>
        /// Contains the queried NAT Type.
        /// Presents if <see cref="QueryError"/> set to <see cref="STUNQueryError.Success"/>
        /// </summary>
        public STUNNATType NATType { get; set; }

        /// <summary>
        /// Contains the public endpoint that queried from server.
        /// </summary>
        public IPEndPoint PublicEndPoint { get; set; }

        /// <summary>
        /// Contains client's socket local endpoiont.
        /// </summary>
        public IPEndPoint LocalEndPoint { get; set; }
    }

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
        public static async Task<STUNQueryResult> QueryAsync(IPEndPoint server, STUNQueryType queryType, bool closeSocket)
        {
            return await Task.Run(() => Query(server, queryType, closeSocket));
        }

        /// <param name="socket">A UDP <see cref="Socket"/> that will use for query. You can also use <see cref="UdpClient.Client"/></param>
        /// <param name="server">Server address</param>
        /// <param name="queryType">Query type</param>
        public static async Task<STUNQueryResult> QueryAsync(Socket socket, IPEndPoint server, STUNQueryType queryType)
        {
            return await Task.Run(() => Query(socket, server, queryType));
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
                socket.Close();
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

        static bool ByteArrayCompare(byte[] b1, byte[] a2)
        {
            return b1.SequenceEqual(a2);
        }
    }
}
