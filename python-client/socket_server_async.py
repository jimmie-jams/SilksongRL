# There is not much reason currently to use this over socket_server.py
# but I'm keeping it in case the training can ever be made to run more 
# than one env at a time. 
# (Don't have the slightest idea how that would work, maybe
# running the game with no graphics?)

import socket
import asyncio
import struct
import json
import time
from enum import IntEnum
from typing import Optional, Dict, Any, Tuple
import numpy as np

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


class AsyncRLSocketServer:
    def __init__(self, host: str = 'localhost', port: int = 8000):
        self.host = host
        self.port = port

    async def start(self, ready_event=None):
        """
        Serve forever. If ready_event is given it is set once we are listening, so a caller can
        wait before starting the game - the mod only retries its connection a handful of times.
        """
        server = await asyncio.start_server(
            self.handle_client,
            self.host,
            self.port
        )
        
        print(f"[AsyncSocketServer] Listening on {self.host}:{self.port}")
        
        if ready_event is not None:
            ready_event.set()
        
        async with server:
            await server.serve_forever()

    async def handle_client(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter):
        addr = writer.get_extra_info('peername')
        print(f"[AsyncSocketServer] Connected by {addr}")
        
        sock = writer.get_extra_info('socket')
        if sock:
            sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        
        try:
            while True:
                result = await self.receive_message(reader)
                
                if result is None:
                    break
                    
                msg_type, payload = result
                
                await self.process_message(msg_type, payload, writer)
        except asyncio.IncompleteReadError:
            print("[AsyncSocketServer] Client disconnected (incomplete read)")
        except Exception as e:
            print(f"[AsyncSocketServer] Error: {e}")
        finally:
            writer.close()
            await writer.wait_closed()
            print("[AsyncSocketServer] Client disconnected")

    async def receive_message(self, reader: asyncio.StreamReader) -> Optional[Tuple[MessageType, Dict]]:
        # Read length prefix (4 bytes, big-endian)
        length_bytes = await reader.readexactly(4)
        length = struct.unpack('>I', length_bytes)[0]
        
        # Read message type (1 byte)
        msg_type_byte = await reader.readexactly(1)
        msg_type = MessageType(msg_type_byte[0])
        
        # Read payload
        payload_length = length - 1
        payload = {}
        if payload_length > 0:
            payload_bytes = await reader.readexactly(payload_length)
            payload = json.loads(payload_bytes.decode('utf-8'))
        
        return msg_type, payload

    async def send_message(self, writer: asyncio.StreamWriter, msg_type: MessageType, payload: Dict[str, Any]):
        payload_bytes = json.dumps(payload).encode('utf-8')
        length = 1 + len(payload_bytes)
        
        full_message = struct.pack('>I', length) + bytes([msg_type]) + payload_bytes
        
        writer.write(full_message)
        await writer.drain()

    async def process_message(self, msg_type: MessageType, payload: Dict[str, Any], 
                              writer: asyncio.StreamWriter):
        try:
            if msg_type == MessageType.INITIALIZE:
                await self.handle_initialize(payload, writer)
            elif msg_type == MessageType.GET_ACTION:
                await self.handle_get_action(payload, writer)
            elif msg_type == MessageType.STORE_TRANSITION:
                await self.handle_store_transition(payload, writer)
        except Exception as e:
            print(f"[AsyncSocketServer] Error processing {msg_type.name}: {e}")
            await self.send_message(writer, MessageType.ERROR, {'error': str(e)})

    async def handle_initialize(self, payload: Dict[str, Any], writer: asyncio.StreamWriter):
        boss_name = payload['boss_name']
        obs_size = payload['observation_size']
        action_space_shape = payload.get('action_space_shape')
        observation_type = payload.get('observation_type', 'vector')
        vector_obs_size = payload.get('vector_obs_size', obs_size)
        visual_width = payload.get('visual_width', 0)
        visual_height = payload.get('visual_height', 0)
        
        print(f"[AsyncSocketServer] Initializing for boss: {boss_name}")
        print(f"[AsyncSocketServer]   Observation size: {obs_size}, type: {observation_type}, vector size: {vector_obs_size}")
        if observation_type == 'hybrid':
            print(f"[AsyncSocketServer]   Visual size: {visual_width}x{visual_height}")
        
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
        await self.send_message(writer, MessageType.INIT_RESPONSE, response)

    async def handle_get_action(self, payload: Dict[str, Any], writer: asyncio.StreamWriter):
        state = payload['state']
        action = rl_get_action(state)
        response = {'action': action}
        await self.send_message(writer, MessageType.ACTION_RESPONSE, response)

    async def handle_store_transition(self, payload: Dict[str, Any], writer: asyncio.StreamWriter):
        state = payload['state']
        action = payload['action']
        reward = payload['reward']
        next_state = payload['next_state']
        done = payload['done']

        rl_store_transition(state, action, reward, next_state, done)
        await self.send_message(writer, MessageType.TRANSITION_ACK, {'success': True})
