<template>
  <div>
    <h2>gRPC-Web ASP.NET Core example</h2>
    <div>
      <span>CORS:</span>
      <select v-model="cors">
        <option value="same">Same Origin</option>
        <option value="different">Different Origin</option>
      </select>
      <span>Hello:</span>
      <input type="text" v-model="name" />
      <p>
        <input type="button" value="Send unary" @click="sendUnary()" />
      </p>
      <p>
        <input
          type="button"
          v-show="!streamingCall"
          value="Start server stream"
          @click="sendServerStream()"
        />
        <input
          type="button"
          v-show="streamingCall"
          value="Stop server stream"
          @click="stopServerStream()"
        />
      </p>
      <p>
        <input
          type="button"
          value="Send unary on server exception"
          @click="sendUnaryServerException()"
        />
      </p>
      <p>
        <input
          type="button"
          value="Send unary on permission denied"
          @click="sendUnaryPermissionDenied()"
        />
      </p>
      <p>
        <input
          type="button"
          value="Send unary on permission denied and null response"
          @click="sendUnaryPermissionDeniedNullResponse()"
        />
      </p>
    </div>
    <div>
      <h4>Unary response:</h4>
      <p>{{result}}</p>
      <h4>Unary errors:</h4>
      <p>{{error}}</p>
      <h4>Stream responses:</h4>
      <p v-for="item in streamResults" :key="item">{{item}}</p>
    </div>
  </div>
</template>

<script lang="ts">
import { Component, Vue } from "vue-property-decorator";
import { GreeterClient } from "./generated/GreetServiceClientPb";
import { HelloRequest } from "./generated/greet_pb";

function getDifferentOrigin() {
  const schema = window.location.protocol as "http:" | "https:";
  const differentSchema = schema === "http:" ? "https" : "http";
  // when port is omitted
  if (!window.location.port) {
    return `${differentSchema}://${window.location.host}`;
  }

  const port = parseInt(window.location.port);

  let differentPort: number;
  if (port === 80) {
    differentPort = 443;
  } else if (port === 443) {
    differentPort = 80;
  } else {
    // handle aspnet dev server
    differentPort = schema === "http:" ? port + 1 : port - 1;
  }

  return `${differentSchema}://${window.location.hostname}:${differentPort}`;
}

@Component({})
export default class GrpcWebDemo extends Vue {
  private defaultClient = new GreeterClient(window.location.origin, null, null);
  private corsClient = new GreeterClient(getDifferentOrigin(), null, null);
  get client() {
    return this.cors === "same" ? this.defaultClient : this.corsClient;
  }
  cors: "same" | "different" = "same";
  name = "";
  result = "";
  error = "";
  streamResults: string[] = [];
  streamingCall: any = null;

  sendUnary() {
    this.result = "";

    const request = new HelloRequest();
    request.setName(this.name);
    this.client.sayHello(request, {}, (err, response) => {
      this.result = response.getMessage();
    });
  }

  sendUnaryServerException() {
    this.error = "";
    const request = new HelloRequest();
    request.setName(this.name);
    this.client.sayHelloServerException(request, {}, (err, response) => {
      this.error = JSON.stringify(err);
    });
  }

  sendUnaryPermissionDenied() {
    this.error = "";
    const request = new HelloRequest();
    request.setName(this.name);
    this.client.sayHelloPermissionDenied(request, {}, (err, response) => {
      this.error = JSON.stringify(err);
    });
  }

  sendUnaryPermissionDeniedNullResponse() {
    this.error = "";
    const request = new HelloRequest();
    request.setName(this.name);
    this.client.sayHelloPermissionDeniedNullResponse(
      request,
      {},
      (err, response) => {
        this.error = JSON.stringify(err);
      }
    );
  }

  sendServerStream() {
    this.streamResults = [];

    const request = new HelloRequest();
    request.setName(this.name);

    this.streamingCall = this.client.sayHellos(request, {});
    this.streamingCall.on("data", response => {
      const result = response.getMessage();
      this.streamResults.push(result);
    });
    this.streamingCall.on("end", function() {});
  }

  stopServerStream() {
    this.streamingCall.cancel();
    this.streamingCall = null;
  }

  mounted() {}
}
</script>

<style lang="scss" scoped>
</style>
