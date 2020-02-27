/**
 * @fileoverview gRPC-Web generated client stub for greet
 * @enhanceable
 * @public
 */

// GENERATED CODE -- DO NOT EDIT!


import * as grpcWeb from 'grpc-web';

import {
  HelloReply,
  HelloRequest} from './greet_pb';

export class GreeterClient {
  client_: grpcWeb.AbstractClientBase;
  hostname_: string;
  credentials_: null | { [index: string]: string; };
  options_: null | { [index: string]: string; };

  constructor (hostname: string,
               credentials?: null | { [index: string]: string; },
               options?: null | { [index: string]: string; }) {
    if (!options) options = {};
    if (!credentials) credentials = {};
    options['format'] = 'text';

    this.client_ = new grpcWeb.GrpcWebClientBase(options);
    this.hostname_ = hostname;
    this.credentials_ = credentials;
    this.options_ = options;
  }

  methodInfoSayHello = new grpcWeb.AbstractClientBase.MethodInfo(
    HelloReply,
    (request: HelloRequest) => {
      return request.serializeBinary();
    },
    HelloReply.deserializeBinary
  );

  sayHello(
    request: HelloRequest,
    metadata: grpcWeb.Metadata | null,
    callback: (err: grpcWeb.Error,
               response: HelloReply) => void) {
    return this.client_.rpcCall(
      this.hostname_ +
        '/greet.Greeter/SayHello',
      request,
      metadata || {},
      this.methodInfoSayHello,
      callback);
  }

  methodInfoSayHellos = new grpcWeb.AbstractClientBase.MethodInfo(
    HelloReply,
    (request: HelloRequest) => {
      return request.serializeBinary();
    },
    HelloReply.deserializeBinary
  );

  sayHellos(
    request: HelloRequest,
    metadata?: grpcWeb.Metadata) {
    return this.client_.serverStreaming(
      this.hostname_ +
        '/greet.Greeter/SayHellos',
      request,
      metadata || {},
      this.methodInfoSayHellos);
  }

  methodInfoSayHelloServerException = new grpcWeb.AbstractClientBase.MethodInfo(
    HelloReply,
    (request: HelloRequest) => {
      return request.serializeBinary();
    },
    HelloReply.deserializeBinary
  );

  sayHelloServerException(
    request: HelloRequest,
    metadata: grpcWeb.Metadata | null,
    callback: (err: grpcWeb.Error,
               response: HelloReply) => void) {
    return this.client_.rpcCall(
      this.hostname_ +
        '/greet.Greeter/SayHelloServerException',
      request,
      metadata || {},
      this.methodInfoSayHelloServerException,
      callback);
  }

  methodInfoSayHelloPermissionDenied = new grpcWeb.AbstractClientBase.MethodInfo(
    HelloReply,
    (request: HelloRequest) => {
      return request.serializeBinary();
    },
    HelloReply.deserializeBinary
  );

  sayHelloPermissionDenied(
    request: HelloRequest,
    metadata: grpcWeb.Metadata | null,
    callback: (err: grpcWeb.Error,
               response: HelloReply) => void) {
    return this.client_.rpcCall(
      this.hostname_ +
        '/greet.Greeter/SayHelloPermissionDenied',
      request,
      metadata || {},
      this.methodInfoSayHelloPermissionDenied,
      callback);
  }

  methodInfoSayHelloPermissionDeniedNullResponse = new grpcWeb.AbstractClientBase.MethodInfo(
    HelloReply,
    (request: HelloRequest) => {
      return request.serializeBinary();
    },
    HelloReply.deserializeBinary
  );

  sayHelloPermissionDeniedNullResponse(
    request: HelloRequest,
    metadata: grpcWeb.Metadata | null,
    callback: (err: grpcWeb.Error,
               response: HelloReply) => void) {
    return this.client_.rpcCall(
      this.hostname_ +
        '/greet.Greeter/SayHelloPermissionDeniedNullResponse',
      request,
      metadata || {},
      this.methodInfoSayHelloPermissionDeniedNullResponse,
      callback);
  }

}

