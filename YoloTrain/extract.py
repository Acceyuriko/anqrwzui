"""Extract every Nth frame (default: 10), center-crop to 640x640, and save to datasets/images.

Usage:
	python extract.py --video path/to/video.mp4 [--output datasets/images] [--size 640] [--stride 10]
"""

import argparse
import math
import time
from pathlib import Path

import cv2


def center_crop(frame, size: int) -> cv2.typing.MatLike:
	"""Return a centered square crop of the given size."""
	height, width = frame.shape[:2]
	if width < size or height < size:
		raise ValueError(f"Frame too small for {size}x{size} crop: {width}x{height}")

	x0 = (width - size) // 2
	y0 = (height - size) // 2
	return frame[y0 : y0 + size, x0 : x0 + size]


def extract_frames(video_path: Path, output_dir: Path, size: int = 640, stride: int = 10) -> int:
	output_dir.mkdir(parents=True, exist_ok=True)

	if stride <= 0:
		raise ValueError("Stride must be a positive integer.")

	cap = cv2.VideoCapture(str(video_path))
	if not cap.isOpened():
		raise RuntimeError(f"Cannot open video: {video_path}")

	saved = 0
	frame_idx = 0
	base_timestamp = int(time.time())

	while True:
		ok, frame = cap.read()
		if not ok or frame is None:
			break

		if frame_idx % stride == 0:
			cropped = center_crop(frame, size)
			out_path = output_dir / f"{base_timestamp}_frame_{frame_idx:06d}.jpg"
			if cv2.imwrite(str(out_path), cropped):
				saved += 1

		frame_idx += 1

	cap.release()
	return saved


def main() -> None:
	parser = argparse.ArgumentParser(description="Extract every Nth center-cropped frame from a video.")
	parser.add_argument("--video", required=True, type=Path, help="Path to the input video file")
	parser.add_argument(
		"--output",
		type=Path,
		default=Path("datasets/images"),
		help="Output directory for extracted frames (default: datasets/images)",
	)
	parser.add_argument("--size", type=int, default=640, help="Crop size in pixels (square). Default: 640")
	parser.add_argument("--stride", type=int, default=10, help="Save every Nth frame. Default: 10")

	args = parser.parse_args()

	saved = extract_frames(args.video, args.output, size=args.size, stride=args.stride)
	print(f"Saved {saved} frames to {args.output}")


if __name__ == "__main__":
	main()
