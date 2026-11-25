"""Graba en MP4 la ejecucion visible de la inspeccion Selenium."""
from __future__ import annotations

import subprocess
import sys
import time
from pathlib import Path

import cv2
import mss
import numpy as np

ROOT = Path(__file__).resolve().parent
VIDEO = ROOT / "Ejecucion_Selenium.mp4"


def main() -> int:
    with mss.mss() as screen:
        monitor = screen.monitors[1]
        width, height = monitor["width"], monitor["height"]
        recorder = cv2.VideoWriter(str(VIDEO), cv2.VideoWriter_fourcc(*"mp4v"), 10, (width, height))
        if not recorder.isOpened():
            raise RuntimeError("No fue posible iniciar el codificador de video MP4.")
        process = subprocess.Popen([sys.executable, "inspeccion_demo_page.py", "--visible", "--pausa", "1"], cwd=ROOT)
        try:
            # Se captura mientras Selenium controla Chrome en una ventana visible.
            while process.poll() is None:
                frame = np.array(screen.grab(monitor))[:, :, :3]
                recorder.write(frame)
                time.sleep(0.1)
        finally:
            recorder.release()
        print(f"Video generado: {VIDEO}")
        return process.returncode or 0


if __name__ == "__main__":
    sys.exit(main())
