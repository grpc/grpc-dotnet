using System;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GRPCServer.Dotnet
{
    public class DefaultGrpcServiceActivator<TGrpcService> : IGrpcServiceActivator<TGrpcService> where TGrpcService : class
    {
        private static readonly Lazy<ObjectFactory> _objectFactory = new Lazy<ObjectFactory>(() => ActivatorUtilities.CreateFactory(typeof(TGrpcService), Type.EmptyTypes));
        private readonly IServiceProvider _serviceProvider;
        private bool? _created;

        public DefaultGrpcServiceActivator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public TGrpcService Create()
        {
            Debug.Assert(!_created.HasValue, "hub activators must not be reused.");

            _created = false;
            var service = _serviceProvider.GetService<TGrpcService>();
            if (service == null)
            {
                service = (TGrpcService)_objectFactory.Value(_serviceProvider, Array.Empty<object>());
                _created = true;
            }

            return service;
        }

        public void Release(TGrpcService service)
        {
        }
    }
}
