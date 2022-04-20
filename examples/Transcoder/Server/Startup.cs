// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Server
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddGrpc()
                .AddJsonTranscoding();

            //services.AddSwaggerGen(c =>
            //{
            //    c.SwaggerDoc("v1", new OpenApiInfo { Title = "gRPC HTTP API Example", Version = "v1" });

            //    var filePath = Path.Combine(System.AppContext.BaseDirectory, "Microsoft.AspNetCore.Grpc.Swagger.Tests.xml");
            //    c.IncludeXmlComments(filePath);

            //    c.IncludeGrpcXmlComments(filePath, includeControllerXmlComments: true);
            //});
            //services.AddGrpcSwagger();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // For static content
            app.UseHttpsRedirection();
            app.UseDefaultFiles();
            app.UseStaticFiles();

            //app.UseSwagger();
            //app.UseSwaggerUI(c =>
            //{
            //    c.SwaggerEndpoint("/swagger/v1/swagger.json", "gRPC HTTP API Example V1");
            //});

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<GreeterService>();
            });
        }
    }
}
