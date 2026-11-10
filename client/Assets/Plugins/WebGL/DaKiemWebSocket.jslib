// The browser's WebSocket for the web build. C# polls these functions once a frame,
// so nothing here calls back into Unity.
mergeInto(LibraryManager.library, {
  DaKiemWsOpen: function (urlPtr) {
    if (!Module.dakiemSockets) {
      Module.dakiemSockets = {};
      Module.dakiemNextSocket = 1;
    }

    var id = Module.dakiemNextSocket++;
    var entry = { socket: null, inbox: [], state: 0, reason: "" };
    Module.dakiemSockets[id] = entry;

    try {
      var socket = new WebSocket(UTF8ToString(urlPtr));
      socket.binaryType = "arraybuffer";
      socket.onopen = function () { entry.state = 1; };
      socket.onmessage = function (event) {
        if (event.data instanceof ArrayBuffer) {
          entry.inbox.push(new Uint8Array(event.data));
        }
      };
      socket.onerror = function () {
        if (!entry.reason) { entry.reason = "lỗi WebSocket"; }
      };
      socket.onclose = function (event) {
        entry.state = 2;
        if (!entry.reason) { entry.reason = event.reason || ("đóng, mã " + event.code); }
      };
      entry.socket = socket;
    } catch (error) {
      entry.state = 2;
      entry.reason = String(error);
    }

    return id;
  },

  // 0 connecting, 1 open, 2 closed.
  DaKiemWsState: function (id) {
    var entry = Module.dakiemSockets && Module.dakiemSockets[id];
    return entry ? entry.state : 2;
  },

  DaKiemWsReason: function (id) {
    var entry = Module.dakiemSockets && Module.dakiemSockets[id];
    var text = entry ? entry.reason : "";
    var size = lengthBytesUTF8(text) + 1;
    var buffer = _malloc(size);
    stringToUTF8(text, buffer, size);
    return buffer;
  },

  DaKiemWsSend: function (id, dataPtr, length) {
    var entry = Module.dakiemSockets && Module.dakiemSockets[id];
    if (entry && entry.state === 1) {
      entry.socket.send(HEAPU8.slice(dataPtr, dataPtr + length));
    }
  },

  // Size of the next message, or 0 when none is waiting.
  DaKiemWsPeek: function (id) {
    var entry = Module.dakiemSockets && Module.dakiemSockets[id];
    return entry && entry.inbox.length > 0 ? entry.inbox[0].length : 0;
  },

  // Copies the next message into the buffer and drops it from the queue.
  DaKiemWsTake: function (id, bufferPtr, length) {
    var entry = Module.dakiemSockets && Module.dakiemSockets[id];
    if (!entry || entry.inbox.length === 0) { return 0; }
    var message = entry.inbox.shift();
    var count = Math.min(length, message.length);
    HEAPU8.set(message.subarray(0, count), bufferPtr);
    return count;
  },

  DaKiemWsClose: function (id) {
    var entry = Module.dakiemSockets && Module.dakiemSockets[id];
    if (!entry) { return; }
    if (entry.socket && entry.state !== 2) { entry.socket.close(1000, "bye"); }
    delete Module.dakiemSockets[id];
  }
});
