"""
face_preprocessor.py — landmark-based face preprocessing for Unity UV transfer.

This script enforces a MediaPipe Face Mesh workflow:
1) Detect 468 landmarks
2) Crop around face
3) Similarity-align eyes/mouth to canonical positions
4) Build a soft alpha from the face-oval contour (remove background/body)
5) Save canonicalized RGBA portrait to Assets/FacePhotos/Processed/
6) Save landmark sidecar JSON for debugging/advanced fitting

Run:
    python dev_tools/face_preprocessor.py

Required:
    pip install opencv-python-headless mediapipe
"""

import json
import os
import sys
from typing import Dict, Tuple

import cv2
import numpy as np

try:
    import mediapipe as mp
except Exception as exc:  # pragma: no cover - runtime dependency
    sys.exit(
        "[ERROR] MediaPipe is required for this workflow.\n"
        "Install: pip install mediapipe\n"
        f"Import error: {exc}"
    )

# Paths
REPO_ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), ".."))
INPUT_DIR = os.path.join(REPO_ROOT, "DevProject", "Assets", "FacePhotos")
OUTPUT_DIR = os.path.join(INPUT_DIR, "Processed")
OUTPUT_SIZE = 512
PADDING_PCT = 0.22

SUPPORTED = {".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff"}

# Debug/sidecar subset
LANDMARK_IDS = [10, 33, 133, 263, 362, 1, 61, 291, 152]

# MediaPipe face oval ring indices (used to build soft alpha mask)
FACE_OVAL_IDS = [
    10,
    338,
    297,
    332,
    284,
    251,
    389,
    356,
    454,
    323,
    361,
    288,
    397,
    365,
    379,
    378,
    400,
    377,
    152,
    148,
    176,
    149,
    150,
    136,
    172,
    58,
    132,
    93,
    234,
    127,
    162,
    21,
    54,
    103,
    67,
    109,
]

# Canonical alignment targets in OUTPUT_SIZE pixel space:
# left eye outer, right eye outer, mouth center
CANON_LEFT_EYE = (170.0, 200.0)
CANON_RIGHT_EYE = (342.0, 200.0)
CANON_MOUTH_CENTER = (256.0, 340.0)


def load_mediapipe_face_mesh():
    return mp.solutions.face_mesh.FaceMesh(
        static_image_mode=True,
        max_num_faces=1,
        refine_landmarks=True,
        min_detection_confidence=0.5,
    )


def detect_landmarks_mediapipe(img_bgr, face_mesh) -> Dict[int, Tuple[float, float]]:
    rgb = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB)
    result = face_mesh.process(rgb)
    if not result.multi_face_landmarks:
        return {}

    face = result.multi_face_landmarks[0]
    ih, iw = img_bgr.shape[:2]
    points = {}
    for i, lm in enumerate(face.landmark):
        points[i] = (float(lm.x * iw), float(lm.y * ih))
    return points


def bounds_from_points(points: Dict[int, Tuple[float, float]]):
    xs = [p[0] for p in points.values()]
    ys = [p[1] for p in points.values()]
    x0 = min(xs)
    y0 = min(ys)
    x1 = max(xs)
    y1 = max(ys)
    return x0, y0, x1, y1


def crop_bounds_with_padding(iw, ih, x0, y0, x1, y1, pad):
    w = max(1.0, x1 - x0)
    h = max(1.0, y1 - y0)
    dx = w * pad
    dy = h * pad

    cx0 = int(max(0, np.floor(x0 - dx)))
    cy0 = int(max(0, np.floor(y0 - dy)))
    cx1 = int(min(iw, np.ceil(x1 + dx)))
    cy1 = int(min(ih, np.ceil(y1 + dy)))
    return cx0, cy0, cx1, cy1


def remap_points_to_cropped(
    points: Dict[int, Tuple[float, float]], x0: int, y0: int, w: int, h: int, out_size: int
):
    sx = out_size / max(1.0, float(w))
    sy = out_size / max(1.0, float(h))
    out = {}
    for idx, (px, py) in points.items():
        out[idx] = ((px - x0) * sx, (py - y0) * sy)
    return out


def similarity_align(img_bgr, points):
    if 33 not in points or 263 not in points or 61 not in points or 291 not in points:
        return img_bgr, points

    left_eye = np.array(points[33], dtype=np.float32)
    right_eye = np.array(points[263], dtype=np.float32)
    mouth_center = (np.array(points[61], dtype=np.float32) + np.array(points[291], dtype=np.float32)) * 0.5

    src_tri = np.array([left_eye, right_eye, mouth_center], dtype=np.float32)
    dst_tri = np.array([CANON_LEFT_EYE, CANON_RIGHT_EYE, CANON_MOUTH_CENTER], dtype=np.float32)
    m = cv2.getAffineTransform(src_tri, dst_tri)

    aligned = cv2.warpAffine(
        img_bgr,
        m,
        (OUTPUT_SIZE, OUTPUT_SIZE),
        flags=cv2.INTER_LINEAR,
        borderMode=cv2.BORDER_REFLECT_101,
    )

    ids = list(points.keys())
    arr = np.array([points[i] for i in ids], dtype=np.float32).reshape(-1, 1, 2)
    arr_t = cv2.transform(arr, m).reshape(-1, 2)
    aligned_points = {ids[i]: (float(arr_t[i, 0]), float(arr_t[i, 1])) for i in range(len(ids))}
    return aligned, aligned_points


def build_soft_face_alpha(points):
    mask = np.zeros((OUTPUT_SIZE, OUTPUT_SIZE), dtype=np.uint8)
    oval = [points[i] for i in FACE_OVAL_IDS if i in points]
    if len(oval) < 3:
        return mask

    poly = np.array(oval, dtype=np.int32)
    hull = cv2.convexHull(poly)
    cv2.fillConvexPoly(mask, hull, 255, lineType=cv2.LINE_AA)

    # Soft edge for seam blending on UV atlas.
    mask = cv2.GaussianBlur(mask, (0, 0), sigmaX=4.0, sigmaY=4.0)
    return mask


def process_image(path, face_mesh):
    img = cv2.imread(path, cv2.IMREAD_COLOR)
    if img is None:
        print(f"  [SKIP] Cannot read: {path}")
        return None, None

    ih, iw = img.shape[:2]
    points = detect_landmarks_mediapipe(img, face_mesh)
    if not points:
        print(f"  [WARN] No face mesh detected: {os.path.basename(path)}")
        return None, None

    x0f, y0f, x1f, y1f = bounds_from_points(points)
    x0, y0, x1, y1 = crop_bounds_with_padding(iw, ih, x0f, y0f, x1f, y1f, PADDING_PCT)
    cropped = img[y0:y1, x0:x1]
    resized = cv2.resize(cropped, (OUTPUT_SIZE, OUTPUT_SIZE), interpolation=cv2.INTER_LANCZOS4)

    cropped_points = remap_points_to_cropped(points, x0, y0, x1 - x0, y1 - y0, OUTPUT_SIZE)
    aligned, aligned_points = similarity_align(resized, cropped_points)
    alpha = build_soft_face_alpha(aligned_points)

    bgra = cv2.cvtColor(aligned, cv2.COLOR_BGR2BGRA)
    bgra[:, :, 3] = alpha

    sidecar = {
        "width": OUTPUT_SIZE,
        "height": OUTPUT_SIZE,
        "detector": "mediapipe_face_mesh",
        "points": [
            {
                "id": int(idx),
                "x": float(np.clip(aligned_points[idx][0] / OUTPUT_SIZE, 0.0, 1.0)),
                "y": float(np.clip(aligned_points[idx][1] / OUTPUT_SIZE, 0.0, 1.0)),
            }
            for idx in LANDMARK_IDS
            if idx in aligned_points
        ],
    }
    return bgra, sidecar


def save_landmarks_json(path: str, payload: dict):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2)


def main():
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    face_mesh = load_mediapipe_face_mesh()
    print("[INFO] MediaPipe Face Mesh enabled.")

    candidates = [
        f
        for f in os.listdir(INPUT_DIR)
        if os.path.isfile(os.path.join(INPUT_DIR, f))
        and os.path.splitext(f)[1].lower() in SUPPORTED
    ]

    if not candidates:
        print(f"[INFO] No images found in {INPUT_DIR}")
        face_mesh.close()
        return

    ok = 0
    for fname in sorted(candidates):
        src = os.path.join(INPUT_DIR, fname)
        stem = os.path.splitext(fname)[0]
        dst = os.path.join(OUTPUT_DIR, f"{stem}_processed.png")
        lm_dst = os.path.join(OUTPUT_DIR, f"{stem}_processed_landmarks.json")

        print(f"Processing: {fname}")
        result, sidecar = process_image(src, face_mesh)
        if result is None:
            continue

        cv2.imwrite(dst, result, [cv2.IMWRITE_PNG_COMPRESSION, 6])
        save_landmarks_json(lm_dst, sidecar)

        print(f"  -> Saved: {os.path.relpath(dst, REPO_ROOT)}")
        ok += 1

    face_mesh.close()
    print(f"\nDone: {ok}/{len(candidates)} face(s) processed -> {OUTPUT_DIR}")
    print("Next step: in Unity run Tools > Face Mapper > 1. Bake Face Materials")


if __name__ == "__main__":
    main()
