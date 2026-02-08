import socket
import struct
import json
import time
from enum import IntEnum
from typing import Optional, Dict, Any

from rl_core import (
    initialize_model,
    get_action as rl_get_action,
    store_transition as rl_store_transition,
)


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
    def __init__(self, host: str = 'localhost', port: int = 8000):
        self.host = host
        self.port = port
        self.socket: Optional[socket.socket] = None
        self.conn: Optional[socket.socket] = None

    def start(self):
        self.socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.socket.bind((self.host, self.port))
        self.socket.listen(1)
        print(f"[SocketServer] Listening on {self.host}:{self.port}")
        
        while True:
            print("[SocketServer] Waiting for connection...")
            self.conn, addr = self.socket.accept()
            self.conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            self.conn.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, 131_072)
            self.conn.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 131_072)
            print(f"[SocketServer] Connected by {addr}")
            self.handle_client()

    def handle_client(self):
        try:
            while True:
                msg_type, payload = self.receive_message()
                
                if msg_type is None:
                    break
                self.process_message(msg_type, payload)
        except Exception as e:
            print(f"[SocketServer] Error: {e}")
        finally:
            if self.conn:
                self.conn.close()
            print("[SocketServer] Client disconnected")

    def receive_message(self):
        length_bytes = self._recv_exact(4)
        if not length_bytes:
            return None, None
        length = struct.unpack('>I', length_bytes)[0]
        
        msg_type_byte = self._recv_exact(1)
        if not msg_type_byte:
            return None, None
        msg_type = MessageType(msg_type_byte[0])
        
        payload_length = length - 1
        payload = {}
        if payload_length > 0:
            payload_bytes = self._recv_exact(payload_length)
            payload = json.loads(payload_bytes.decode('utf-8'))
        
        return msg_type, payload

    def send_message(self, msg_type: MessageType, payload: Dict[str, Any]):
        payload_bytes = json.dumps(payload).encode('utf-8')
        length = 1 + len(payload_bytes)
        
        full_message = struct.pack('>I', length) + bytes([msg_type]) + payload_bytes
        self.conn.sendall(full_message)

    def _recv_exact(self, n: int) -> Optional[bytes]:
        data = b''
        while len(data) < n:
            chunk = self.conn.recv(n - len(data))
            if not chunk:
                return None
            data += chunk
        return data

    def process_message(self, msg_type: MessageType, payload: Dict[str, Any]):
        try:
            if msg_type == MessageType.INITIALIZE:
                self.handle_initialize(payload)
            elif msg_type == MessageType.GET_ACTION:
                self.handle_get_action(payload)
            elif msg_type == MessageType.STORE_TRANSITION:
                self.handle_store_transition(payload)
        except Exception as e:
            print(f"[SocketServer] Error processing {msg_type.name}: {e}")
            self.send_message(MessageType.ERROR, {'error': str(e)})

    def handle_initialize(self, payload: Dict[str, Any]):
        boss_name = payload['boss_name']
        obs_size = payload['observation_size']
        action_space_shape = payload.get('action_space_shape')
        observation_type = payload.get('observation_type', 'vector')
        vector_obs_size = payload.get('vector_obs_size', obs_size)
        visual_width = payload.get('visual_width', 0)
        visual_height = payload.get('visual_height', 0)
        
        print(f"[SocketServer] Initializing for boss: {boss_name}")
        print(f"[SocketServer]   Observation size: {obs_size}, type: {observation_type}, vector size: {vector_obs_size}")
        if observation_type == 'hybrid':
            print(f"[SocketServer]   Visual size: {visual_width}x{visual_height}")
        
        init_response = initialize_model(
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
        self.send_message(MessageType.INIT_RESPONSE, response)

    def handle_get_action(self, payload: Dict[str, Any]):
        state = payload['state']
        action = rl_get_action(state)
        response = {'action': action}
        self.send_message(MessageType.ACTION_RESPONSE, response)

    def handle_store_transition(self, payload: Dict[str, Any]):
        state = payload['state']
        action = payload['action']
        reward = payload['reward']
        next_state = payload['next_state']
        done = payload['done']

        rl_store_transition(state, action, reward, next_state, done)
        self.send_message(MessageType.TRANSITION_ACK, {'success': True})


