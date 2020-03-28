# gRPC-Web generated client

gRPC-Web JavaScript clients and messages are generated using `protoc` the gRPC-Web code generator plugin. Instructions for using it are available [here](https://github.com/grpc/grpc-web#code-generator-plugin).

Files in this directory are generated from *greet.proto*:

* *greet_grpc_web_pb.js* contains the gRPC-Web client.
* *greet_pb.js* contains gRPC messages.

Example of using `protoc` from PowerShell (`protoc` and `protoc-gen-grpc-web` should be on your computer and discoverable from your PATH):

> protoc greet.proto --js_out=import_style=commonjs:CHANGE_TO_SCRIPTS_DIRECTORY --grpc-web_out=import_style=commonjs,mode=grpcwebtext:CHANGE_TO_SCRIPTS_DIRECTORY --plugin=protoc-gen-grpc-web=CHANGE_TO_PROTOC_GEN_GRPC_WEB_EXE_PATH
