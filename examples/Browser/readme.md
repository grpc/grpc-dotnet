# Using this sample with App Service Linux

If you plan to deploy this sample to Azure App Service on a Linux App Service plan, the `Protocols` variable defined in `appsettings.json` should be set to a value of `Http1`.

For App Service scenarios, **HTTP2** communication is terminated at the service front ends. Front end to worker communication is done over **Http 1.1**.
