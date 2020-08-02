using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Shared;
using ISerializer = OmniSharp.Extensions.LanguageServer.Protocol.Serialization.ISerializer;

namespace OmniSharp.Extensions.LanguageServer.Shared
{
    internal class LspRequestRouter : RequestRouterBase<ILspHandlerDescriptor>, IRequestRouter<IHandlerDescriptor>
    {
        private readonly IHandlerCollection _collection;
        private readonly HashSet<IHandlerMatcher> _handlerMatchers;

        public LspRequestRouter(
            IHandlerCollection collection,
            ILoggerFactory loggerFactory,
            IEnumerable<IHandlerMatcher> handlerMatchers,
            ISerializer serializer,
            IServiceProvider serviceProvider,
            IServiceScopeFactory serviceScopeFactory) :
            base(serializer, serviceProvider, serviceScopeFactory, loggerFactory.CreateLogger<LspRequestRouter>())
        {
            _collection = collection;
            _handlerMatchers = new HashSet<IHandlerMatcher>(handlerMatchers);
        }

        public override IRequestDescriptor<ILspHandlerDescriptor> GetDescriptors(Notification notification)
        {
            return FindDescriptor(notification);
        }

        public override IRequestDescriptor<ILspHandlerDescriptor> GetDescriptors(Request request)
        {
            return FindDescriptor(request);
        }

        private IRequestDescriptor<ILspHandlerDescriptor> FindDescriptor(IMethodWithParams instance)
        {
            return FindDescriptor(instance.Method, instance.Params);
        }

        private IRequestDescriptor<ILspHandlerDescriptor> FindDescriptor(string method, JToken @params)
        {
            _logger.LogDebug("Finding descriptors for {Method}", method);
            var descriptor = _collection.FirstOrDefault(x => x.Method == method);
            if (descriptor is null)
            {
                _logger.LogDebug("Unable to find {Method}, methods found include {Methods}", method,
                    string.Join(", ", _collection.Select(x => x.Method + ":" + x.Handler?.GetType()?.FullName)));
                return new RequestDescriptor<ILspHandlerDescriptor>(null, Array.Empty<ILspHandlerDescriptor>());
            }

            var paramsValue = DeserializeParams(descriptor, @params);
            if (@params == null || descriptor.Params == null) return new RequestDescriptor<ILspHandlerDescriptor>(paramsValue, descriptor);

            var lspHandlerDescriptors = _collection.Where(handler => handler.Method == method).ToList();

            var matchDescriptor = _handlerMatchers.SelectMany(strat => strat.FindHandler(paramsValue, lspHandlerDescriptors)).ToArray();
            if (matchDescriptor.Length > 0) return new RequestDescriptor<ILspHandlerDescriptor>(paramsValue, matchDescriptor);
            // execute command is a special case
            // if no command was found to execute this must error
            // this is not great coupling but other options require api changes
            if (paramsValue is ExecuteCommandParams) return new RequestDescriptor<ILspHandlerDescriptor>(paramsValue, Array.Empty<ILspHandlerDescriptor>());
            if (lspHandlerDescriptors.Count > 0) return new RequestDescriptor<ILspHandlerDescriptor>(paramsValue, lspHandlerDescriptors);
            return new RequestDescriptor<ILspHandlerDescriptor>(paramsValue, Array.Empty<ILspHandlerDescriptor>());
        }

        IRequestDescriptor<IHandlerDescriptor> IRequestRouter<IHandlerDescriptor>.GetDescriptors(Notification notification) => GetDescriptors(notification);
        IRequestDescriptor<IHandlerDescriptor> IRequestRouter<IHandlerDescriptor>.GetDescriptors(Request request) => GetDescriptors(request);

        Task IRequestRouter<IHandlerDescriptor>.RouteNotification(IRequestDescriptor<IHandlerDescriptor> descriptors, Notification notification, object @params, CancellationToken token) =>
            RouteNotification(
                descriptors is IRequestDescriptor<ILspHandlerDescriptor> d ? d : throw new Exception("This should really never happen, seriously, only hand this correct descriptors"),
                notification,
                @params,
                token);

        Task<ErrorResponse> IRequestRouter<IHandlerDescriptor>.RouteRequest(IRequestDescriptor<IHandlerDescriptor> descriptors, Request request, object @params, CancellationToken token) =>
            RouteRequest(
                descriptors is IRequestDescriptor<ILspHandlerDescriptor> d ? d : throw new Exception("This should really never happen, seriously, only hand this correct descriptors"),
                request,
                @params,
                token);
    }
}
