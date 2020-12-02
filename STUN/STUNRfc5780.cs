using System.Linq;
using System.Net;
using System.Net.Sockets;
using STUN.Attributes;

namespace STUN
{
    public class STUNRfc5780
    {
        public static STUNQueryResult Query(Socket socket, IPEndPoint server, STUNQueryType queryType, int ReceiveTimeout)
        {
            STUNNatMappingBehavior mappingBehavior = STUNNatMappingBehavior.NoMapping;
            STUNNatFilteringBehavior filteringBehavior = STUNNatFilteringBehavior.EndpointIndependentFiltering;
            var result = new STUNQueryResult(); // the query result
            result.Socket = socket;
            result.ServerEndPoint = server;
            result.NATType = STUNNATType.Unspecified;
            result.QueryType = queryType;

            var transID = STUNMessage.GenerateTransactionIDNewStun(); // get a random trans id
            var message = new STUNMessage(STUNMessageTypes.BindingRequest, transID); // create a bind request
            // send the request to server
            socket.SendTo(message.GetBytes(), server);
            // we set result local endpoint after calling SendTo,
            // because if socket is unbound, the system will bind it after SendTo call.
            result.LocalEndPoint = socket.LocalEndPoint as IPEndPoint;

            // wait for response
            var responseBuffer = STUNUtils.Receive(socket, ReceiveTimeout);

            // didn't receive anything
            if (responseBuffer == null)
            {
                result.QueryError = STUNQueryError.Timeout;
                return result;
            }

            // try to parse message
            if (!message.TryParse(responseBuffer))
            {
                result.QueryError = STUNQueryError.BadResponse;
                return result;
            }

            // check trans id
            if (!STUNUtils.ByteArrayCompare(message.TransactionID, transID))
            {
                result.QueryError = STUNQueryError.BadTransactionID;
                return result;
            }

            // finds error-code attribute, used in case of binding error
            var errorAttr = message.Attributes.FirstOrDefault(p => p is STUNErrorCodeAttribute)
                as STUNErrorCodeAttribute;

            // if server responded our request with error
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

            // return if receive something else than binding response
            if (message.MessageType != STUNMessageTypes.BindingResponse)
            {
                result.QueryError = STUNQueryError.BadResponse;
                return result;
            }

            var xorAddressAttribute = message.Attributes.FirstOrDefault(p => p is STUNXorMappedAddressAttribute)
                as STUNXorMappedAddressAttribute;

            if (xorAddressAttribute == null)
            {
                result.QueryError = STUNQueryError.BadResponse;
                return result;
            }

            result.PublicEndPoint = xorAddressAttribute.EndPoint;

            // stop querying and return the public ip if user just wanted to know public ip
            if (queryType == STUNQueryType.PublicIP)
            {
                result.QueryError = STUNQueryError.Success;
                return result;
            }

            var otherAddressAttribute = message.Attributes.FirstOrDefault(p => p is STUNOtherAddressAttribute)
                as STUNOtherAddressAttribute;

            var changedAddressAttribute = message.Attributes.FirstOrDefault(p => p is STUNChangedAddressAttribute)
                as STUNChangedAddressAttribute;
            // Check is next test should be performed and is support rfc5780 test
            if (otherAddressAttribute == null)
            {
                if (changedAddressAttribute == null)
                {
                    result.QueryError = STUNQueryError.NotSupported;
                    return result;
                }

                otherAddressAttribute = new STUNOtherAddressAttribute();
                otherAddressAttribute.EndPoint = changedAddressAttribute.EndPoint;
            }

            // Make test 2 - bind different ip address but primary port
            message = new STUNMessage(STUNMessageTypes.BindingRequest, transID); // create a bind request
            IPEndPoint secondaryServer = new IPEndPoint(otherAddressAttribute.EndPoint.Address, server.Port);

            socket.SendTo(message.GetBytes(), secondaryServer);
            responseBuffer = STUNUtils.Receive(socket, ReceiveTimeout);

            // Secondary server presented but is down
            if (responseBuffer == null)
            {
                result.QueryError = STUNQueryError.Timeout;
                return result;
            }

            if (!message.TryParse(responseBuffer))
            {
                result.QueryError = STUNQueryError.BadResponse;
                return result;
            }

            var xorAddressAttribute2 = message.Attributes.FirstOrDefault(p => p is STUNXorMappedAddressAttribute)
                as STUNXorMappedAddressAttribute;

            if (xorAddressAttribute2 == null)
            {
                result.QueryError = STUNQueryError.BadResponse;
                return result;
            }

            if (xorAddressAttribute.EndPoint.Equals(xorAddressAttribute2.EndPoint))
            {
                if (xorAddressAttribute.EndPoint.Equals(socket.LocalEndPoint) ||
                    Dns.GetHostAddresses(Dns.GetHostName()).Contains(xorAddressAttribute.EndPoint.Address)
                )
                {
                    mappingBehavior = STUNNatMappingBehavior.NoMapping;
                }
                else
                {
                    mappingBehavior = STUNNatMappingBehavior.EndpointIndependentMapping;
                }
            }
            else // Make test 3
            {
                IPEndPoint secondaryServerPort = new IPEndPoint(otherAddressAttribute.EndPoint.Address,
                    otherAddressAttribute.EndPoint.Port);

                message = new STUNMessage(STUNMessageTypes.BindingRequest, transID); // create a bind request
                socket.SendTo(message.GetBytes(), secondaryServerPort);
                responseBuffer = STUNUtils.Receive(socket, ReceiveTimeout);

                if (responseBuffer == null)
                {
                    result.QueryError = STUNQueryError.Timeout;
                    return result;
                }

                if (!message.TryParse(responseBuffer))
                {
                    result.QueryError = STUNQueryError.BadResponse;
                    return result;
                }

                var xorAddressAttribute3 = message.Attributes.FirstOrDefault(p => p is STUNXorMappedAddressAttribute)
                    as STUNXorMappedAddressAttribute;

                if (xorAddressAttribute3 == null)
                {
                    result.QueryError = STUNQueryError.BadResponse;
                    return result;
                }

                if (xorAddressAttribute3.EndPoint.Equals(xorAddressAttribute2.EndPoint))
                {
                    mappingBehavior = STUNNatMappingBehavior.AddressDependMapping;
                }
                else
                {
                    mappingBehavior = STUNNatMappingBehavior.AddressAndPortDependMapping;
                }
            }

            // Now make a filtering behavioral test
            // We already made a test 1 for mapping behavioral
            // so jump to test 2

            // Send message to primary server.
            // Try receive from another server and port
            message = new STUNMessage(STUNMessageTypes.BindingRequest, transID);
            message.Attributes.Add(new STUNChangeRequestAttribute(true, true));

            socket.SendTo(message.GetBytes(), server);

            responseBuffer = STUNUtils.Receive(socket, ReceiveTimeout);

            if (responseBuffer != null)
            {
                filteringBehavior = STUNNatFilteringBehavior.EndpointIndependentFiltering;
            }
            else // Test 3 - send request to original server with change port attribute
            {
                message = new STUNMessage(STUNMessageTypes.BindingRequest, transID);
                message.Attributes.Add(new STUNChangeRequestAttribute(false, true));

                socket.SendTo(message.GetBytes(), server);

                responseBuffer = STUNUtils.Receive(socket, ReceiveTimeout);

                if (responseBuffer != null)
                {
                    filteringBehavior = STUNNatFilteringBehavior.AddressDependFiltering;
                }
                else
                {
                    filteringBehavior = STUNNatFilteringBehavior.AddressAndPortDependFiltering;
                }
            }

            result.FilteringBehavior = filteringBehavior;
            if (mappingBehavior == STUNNatMappingBehavior.NoMapping)
            {
                result.NATType = STUNNATType.OpenInternet;
            }
            else if (mappingBehavior == STUNNatMappingBehavior.EndpointIndependentMapping)
            {
                if (filteringBehavior == STUNNatFilteringBehavior.EndpointIndependentFiltering)
                {
                    result.NATType = STUNNATType.FullCone;
                }
                else if (filteringBehavior == STUNNatFilteringBehavior.AddressDependFiltering)
                {
                    result.NATType = STUNNATType.Restricted;
                }
                else // (filteringBehavior == STUNNatFilteringBehavior.AddressAndPortDependFiltering)
                {
                    result.NATType = STUNNATType.PortRestricted;
                }
            }
            else
            {
                result.NATType = STUNNATType.Symmetric;
            }

            return result;
        }
    }
}