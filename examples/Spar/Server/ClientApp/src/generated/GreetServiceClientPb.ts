/**
 * @fileoverview gRPC-Web generated client stub for greet
 * @enhanceable
 * @public
 */

// GENERATED CODE -- DO NOT EDIT!


/* eslint-disable */
// @ts-nocheck


import * as grpcWeb from 'grpc-web';

import * as greet_pb from './greet_pb';


export class GreeterClient {
  client_: grpcWeb.AbstractClientBase;
  hostname_: string;
  credentials_: null | { [index: string]: string; };
  options_: null | { [index: string]: any; };

  constructor (hostname: string,
               credentials?: null | { [index: string]: string; },
               options?: null | { [index: string]: any; }) {
    if (!options) options = {};
    if (!credentials) credentials = {};
    options['format'] = 'text';

    this.client_ = new grpcWeb.GrpcWebClientBase(options);
    this.hostname_ = hostname;
    this.credentials_ = credentials;
    this.options_ = options;
  }

  methodInfoSayHello = new grpcWeb.AbstractClientBase.MethodInfo(
    greet_pb.HelloReply,
    (request: greet_pb.HelloRequest) => {
      return request.serializeBinary();
    },
    greet_pb.HelloReply.deserializeBinary
  );

  sayHello(
    request: greet_pb.HelloRequest,
    metadata: grpcWeb.Metadata | null): Promise<greet_pb.HelloReply>;

  sayHello(
    request: greet_pb.HelloRequest,
    metadata: grpcWeb.Metadata | null,
    callback: (err: grpcWeb.Error,
               response: greet_pb.HelloReply) => void): grpcWeb.ClientReadableStream<greet_pb.HelloReply>;

  sayHello(
    request: greet_pb.HelloRequest,
    metadata: grpcWeb.Metadata | null,
    callback?: (err: grpcWeb.Error,
               response: greet_pb.HelloReply) => void) {
    if (callback !== undefined) {
      return this.client_.rpcCall(
        this.hostname_ +
          '/greet.Greeter/SayHello',
        request,
        metadata || {},
        this.methodInfoSayHello,
        callback);
    }
    return this.client_.unaryCall(
    this.hostname_ +
      '/greet.Greeter/SayHello',
    request,
    metadata || {},
    this.methodInfoSayHello);
  }

  methodInfoSayHellos = new grpcWeb.AbstractClientBase.MethodInfo(
    greet_pb.HelloReply,
    (request: greet_pb.HelloRequest) => {
      return request.serializeBinary();
    },
    greet_pb.HelloReply.deserializeBinary
  );

  sayHellos(
    request: greet_pb.HelloRequest,
    metadata?: grpcWeb.Metadata) {
    return this.client_.serverStreaming(
      this.hostname_ +
        '/greet.Greeter/SayHellos',
      request,
      metadata || {},
      this.methodInfoSayHellos);
  }

}

