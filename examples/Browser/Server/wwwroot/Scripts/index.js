const { HelloRequest, HelloReply } = require('./greet_pb.js');
const { GreeterClient } = require('./greet_grpc_web_pb.js');

var client = new GreeterClient(window.location.origin);

var nameInput = document.getElementById('name');
var sendInput = document.getElementById('send');
var streamInput = document.getElementById('stream');
var resultText = document.getElementById('result');
var streamingCall = null;

// Unary call
sendInput.onclick = function () {
    var request = new HelloRequest();
    request.setName(nameInput.value);

    client.sayHello(request, {}, (err, response) => {
        resultText.innerHTML = htmlEscape(response.getMessage());
    });
};

// Server streaming call
streamInput.onclick = function () {
    if (!streamingCall) {
        sendInput.disabled = true;
        streamInput.value = 'Stop server stream';
        resultText.innerHTML = '';

        var request = new HelloRequest();
        request.setName(nameInput.value);

        streamingCall = client.sayHellos(request, {});
        streamingCall.on('data', (response) => {
            resultText.innerHTML += htmlEscape(response.getMessage()) + '<br />';
        });
        streamingCall.on('status', (status) => {
            if (status.code == 0) {
                resultText.innerHTML += 'Done';
            } else {
                resultText.innerHTML += 'Error: ' + htmlEscape(status.details);
            }
        });
    } else {
        streamingCall.cancel();
        streamingCall = null;
        sendInput.disabled = false;
        streamInput.value = 'Start server stream';
    }
};

function htmlEscape(str) {
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}