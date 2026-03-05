"""
face_preprocessor.py — Pre-process face photos before Unity import.

Reads every image in Assets/FacePhotos/, detects the face with OpenCV
Haar cascade, crops it with padding, resizes to 512x512, and saves
to Assets/FacePhotos/Processed/.

Run once before using the Unity editor bake tool:
    python dev_tools/face_preprocessor.py

Dependencies: opencv-python (already in mlagents env via standard install)
If missing:   pip install opencv-python-headless
"""

import cv2
import os
import sys
import numpy as np

# ── Paths ─────────────────────────────────────────────────────────────────
REPO_ROOT   = os.path.normpath(os.path.join(os.path.dirname(__file__), ".."))
INPUT_DIR   = os.path.join(REPO_ROOT, "DevProject", "Assets", "FacePhotos")
OUTPUT_DIR  = os.path.join(INPUT_DIR, "Processed")
OUTPUT_SIZE = 512           # Unity power-of-two texture size
PADDING_PCT = 0.25          # Extra space around detected face bbox (25 %)

SUPPORTED = {".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff"}

# OpenCV bundled frontal-face detector (no extra download needed)
CASCADE_PATH = cv2.data.haarcascades + "haarcascade_frontalface_alt2.xml"


def load_detector():
    if not os.path.exists(CASCADE_PATH):
        sys.exit(f"[ERROR] Haar cascade not found at: {CASCADE_PATH}\n"
                 "Install opencv-python: pip install opencv-python-headless")
    detector = cv2.CascadeClassifier(CASCADE_PATH)
    if detector.empty():
        sys.exit("[ERROR] Failed to load Haar cascade.")
    return detector


def detect_face_rect(img_bgr, detector):
    """Return (x, y, w, h) of the largest detected face, or None."""
    gray = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2GRAY)
    gray = cv2.equalizeHist(gray)
    faces = detector.detectMultiScale(
        gray,
        scaleFactor=1.1,
        minNeighbors=4,
        minSize=(60, 60),
        flags=cv2.CASCADE_SCALE_IMAGE,
    )
    if len(faces) == 0:
        return None
    # Pick largest face
    return max(faces, key=lambda r: r[2] * r[3])


def crop_with_padding(img, x, y, w, h, pad=PADDING_PCT):
    ih, iw = img.shape[:2]
    dx = int(w * pad)
    dy = int(h * pad)
    x0 = max(0, x - dx)
    y0 = max(0, y - dy)
    x1 = min(iw, x + w + dx)
    y1 = min(ih, y + h + dy)
    return img[y0:y1, x0:x1]


def process_image(path, detector):
    img = cv2.imread(path, cv2.IMREAD_COLOR)
    if img is None:
        print(f"  [SKIP] Cannot read: {path}")
        return None

    face = detect_face_rect(img, detector)
    if face is None:
        print(f"  [WARN] No face detected — using centre crop: {os.path.basename(path)}")
        # Fallback: centre-square crop
        ih, iw = img.shape[:2]
        side = min(ih, iw)
        x0 = (iw - side) // 2
        y0 = (ih - side) // 2
        cropped = img[y0:y0 + side, x0:x0 + side]
    else:
        x, y, w, h = face
        cropped = crop_with_padding(img, x, y, w, h)

    resized = cv2.resize(cropped, (OUTPUT_SIZE, OUTPUT_SIZE), interpolation=cv2.INTER_LANCZOS4)
    return resized


def main():
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    detector = load_detector()

    candidates = [
        f for f in os.listdir(INPUT_DIR)
        if os.path.isfile(os.path.join(INPUT_DIR, f))
        and os.path.splitext(f)[1].lower() in SUPPORTED
    ]

    if not candidates:
        print(f"[INFO] No images found in {INPUT_DIR}")
        return

    ok = 0
    for fname in sorted(candidates):
        src = os.path.join(INPUT_DIR, fname)
        stem = os.path.splitext(fname)[0]
        dst = os.path.join(OUTPUT_DIR, f"{stem}_processed.png")

        print(f"Processing: {fname}")
        result = process_image(src, detector)
        if result is None:
            continue

        cv2.imwrite(dst, result, [cv2.IMWRITE_PNG_COMPRESSION, 6])
        print(f"  → Saved: {os.path.relpath(dst, REPO_ROOT)}")
        ok += 1

    print(f"\nDone: {ok}/{len(candidates)} face(s) processed → {OUTPUT_DIR}")
    print("Next step: in Unity run  Tools > Face Mapper > 1. Bake Face Materials")


if __name__ == "__main__":
    main()
