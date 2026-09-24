import itertools
import socket
import struct
import json
import threading
from enum import IntEnum
from typing import Optional, Dict, Any

import rl_core


class MessageType(IntEnum):
    # Client -> Server
    INITIALIZE = 0
    GET_ACTION = 1
    STORE_TRANSITION = 2
    # Server -> Client
    INIT_RESPONSE = 10
    ACTION_RESPONSE = 11
    TRANSITION_ACK = 12
    ERROR = 255


class RLSocketServer:
    """
    Serves any number of games at once, each on its own thread, all driving the one model.
    rl_core is not thread safe, so every call into it holds model_lock.
    """

    def __init__(self, host: str = 'localhost', port: int = 8000):
        self.host = host
        self.port = port
        self.model_lock = threading.Lock()
        # A connection is a game as far as training goes. One that reconnects gets a new id.
        self._client_ids = itertools.count(1)

    def start(self, ready_event=None):
        """
        Serve forever. If ready_event is given it is set once we are listening, so a caller can
        wait before starting the game - the mod only retries its connection a handful of times.
        """
        server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        server.bind((self.host, self.port))
        server.listen()
        print(f"[SocketServer] Listening on {self.host}:{self.port}")

        if ready_event is not None:
            ready_event.set()

        while True:
            conn, addr = server.accept()
            client_id = next(self._client_ids)
            threading.Thread(target=self.handle_client, args=(conn, addr, client_id),
                             name=f"client-{client_id}", daemon=True).start()

    def handle_client(self, conn: socket.socket, addr, client_id: int):
        conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        conn.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, 131_072)
        conn.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 131_072)
        print(f"[SocketServer] Client {client_id} connected from {addr}")
        try:
            while True:
                msg_type, payload = self.receive_message(conn)
                if msg_type is None:
                    break
                self.process_message(conn, client_id, msg_type, payload)
        except Exception as e:
            print(f"[SocketServer] Client {client_id} error: {e}")
        finally:
            conn.close()
            with self.model_lock:
                rl_core.remove_game(client_id)
            print(f"[SocketServer] Client {client_id} disconnected")

    def receive_message(self, conn: socket.socket):
        length_bytes = self._recv_exact(conn, 4)
        if not length_bytes:
            return None, None
        length = struct.unpack('>I', length_bytes)[0]

        msg_type_byte = self._recv_exact(conn, 1)
        if not msg_type_byte:
            return None, None
        msg_type = MessageType(msg_type_byte[0])

        payload_length = length - 1
        payload = {}
        if payload_length > 0:
            payload_bytes = self._recv_exact(conn, payload_length)
            if payload_bytes is None:
                return None, None
            payload = json.loads(payload_bytes.decode('utf-8'))

        return msg_type, payload

    def send_message(self, conn: socket.socket, msg_type: MessageType, payload: Dict[str, Any]):
        payload_bytes = json.dumps(payload).encode('utf-8')
        length = 1 + len(payload_bytes)

        full_message = struct.pack('>I', length) + bytes([msg_type]) + payload_bytes
        conn.sendall(full_message)

    def _recv_exact(self, conn: socket.socket, n: int) -> Optional[bytes]:
        data = b''
        while len(data) < n:
            chunk = conn.recv(n - len(data))
            if not chunk:
                return None
            data += chunk
        return data

    def process_message(self, conn: socket.socket, client_id: int, msg_type: MessageType,
                        payload: Dict[str, Any]):
        try:
            if msg_type == MessageType.INITIALIZE:
                self.handle_initialize(conn, client_id, payload)
            elif msg_type == MessageType.GET_ACTION:
                self.handle_get_action(conn, payload)
            elif msg_type == MessageType.STORE_TRANSITION:
                self.handle_store_transition(conn, client_id, payload)
        except Exception as e:
            print(f"[SocketServer] Client {client_id}: error processing {msg_type.name}: {e}")
            self.send_message(conn, MessageType.ERROR, {'error': str(e)})

    def handle_initialize(self, conn: socket.socket, client_id: int, payload: Dict[str, Any]):
        boss_name = payload['boss_name']
        obs_size = payload['observation_size']
        action_space_shape = payload.get('action_space_shape')
        observation_type = payload.get('observation_type', 'vector')
        vector_obs_size = payload.get('vector_obs_size', obs_size)
        visual_width = payload.get('visual_width', 0)
        visual_height = payload.get('visual_height', 0)

        print(f"[SocketServer] Client {client_id} initializing for boss: {boss_name}")
        print(f"[SocketServer]   Observation size: {obs_size}, type: {observation_type}, vector size: {vector_obs_size}")
        if observation_type == 'hybrid':
            print(f"[SocketServer]   Visual size: {visual_width}x{visual_height}")

        with self.model_lock:
            init_response = rl_core.initialize_model(
                obs_size,
                boss_name,
                action_space_shape,
                observation_type=observation_type,
                vector_obs_size=vector_obs_size,
                visual_w=visual_width,
                visual_h=visual_height
            )

        response = {
            'initialized': init_response["initialized"],
            'boss_name': init_response["boss_name"],
            'observation_size': init_response["observation_size"],
            'checkpoint_loaded': init_response["checkpoint_loaded"]
        }
        self.send_message(conn, MessageType.INIT_RESPONSE, response)

    def handle_get_action(self, conn: socket.socket, payload: Dict[str, Any]):
        with self.model_lock:
            action = rl_core.get_action(payload['state'])
        self.send_message(conn, MessageType.ACTION_RESPONSE, {'action': action})

    def handle_store_transition(self, conn: socket.socket, client_id: int, payload: Dict[str, Any]):
        with self.model_lock:
            rl_core.store_transition(
                client_id,
                payload['state'],
                payload['action'],
                payload['reward'],
                payload['next_state'],
                payload['done'],
            )
        self.send_message(conn, MessageType.TRANSITION_ACK, {'success': True})
