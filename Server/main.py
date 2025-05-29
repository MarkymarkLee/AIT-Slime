import socket
import threading
import time

HOST = '0.0.0.0'  # Listen on all interfaces
PORT = 5005       # Port to listen on (must match Unity client)

def handle_client(conn, addr):
    print(f"Connected by {addr}")
    try:
        while True:
            status = "Server is running"
            conn.sendall(status.encode('utf-8'))
            time.sleep(1)  # Send status every second
    except (ConnectionResetError, BrokenPipeError):
        print(f"Connection with {addr} closed.")
    finally:
        conn.close()

def start_server():
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind((HOST, PORT))
        s.listen()
        print(f"Server listening on {HOST}:{PORT}")
        while True:
            conn, addr = s.accept()
            client_thread = threading.Thread(target=handle_client, args=(conn, addr), daemon=True)
            client_thread.start()

if __name__ == "__main__":
    start_server()