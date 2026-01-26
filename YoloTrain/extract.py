"""Extract every Nth frame (default: 10), center-crop to 640x640, and save to datasets/images.

Usage:
	python extract.py --video path/to/video_or_dir [--output datasets/images] [--size 640] [--stride 10]
"""

import argparse
import time
from pathlib import Path
import shutil
import subprocess

def _extract_with_ffmpeg(video_path: Path, output_dir: Path, size: int = 640, stride: int = 10) -> int:
	output_dir.mkdir(parents=True, exist_ok=True)

	if stride <= 0:
		raise ValueError("Stride must be a positive integer.")

	ffmpeg_exe = shutil.which("ffmpeg")
	if not ffmpeg_exe:
		raise RuntimeError("ffmpeg not found in PATH. Install ffmpeg to enable AV1 fallback.")

	# Use ffmpeg to select every Nth frame and center-crop to size
	# -vsync 0 prevents frame duplication, -q:v 2 gives good JPEG quality
	vf = f"select=not(mod(n\\,{stride})),crop={size}:{size}:(iw-{size})/2:(ih-{size})/2"

	# Output files as sequential images. Use timestamp prefix to avoid collisions.
	base_timestamp = int(time.time())
	out_pattern = str(output_dir / f"{base_timestamp}_frame_%06d.jpg")

	def run_ffmpeg(decoder: str | None) -> tuple[int, bytes]:
		cmd = [
			ffmpeg_exe,
			"-hide_banner",
			"-loglevel",
			"error",
		]
		if decoder:
			# libdav1d is often more robust for AV1; -err_detect ignore_err skips broken frames.
			cmd += ["-c:v", decoder, "-err_detect", "ignore_err"]
		cmd += [
			"-i",
			str(video_path),
			"-vf",
			vf,
			"-vsync",
			"0",
			"-q:v",
			"2",
			out_pattern,
		]
		proc = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
		return proc.returncode, proc.stderr

	# First try libdav1d decoder (better AV1 support); if it fails, fall back to ffmpeg default.
	code, stderr = run_ffmpeg("libdav1d")
	if code != 0:
		code, stderr = run_ffmpeg(None)

	if code != 0:
		raise RuntimeError(f"ffmpeg failed: {stderr.decode(errors='replace')}")

	# Count created files that match the prefix
	created = len(list(output_dir.glob(f"{base_timestamp}_frame_*.jpg")))
	return created


def extract_frames(video_path: Path, output_dir: Path, size: int = 640, stride: int = 10) -> int:
	"""Extract frames using ffmpeg only (supports AV1)."""
	return _extract_with_ffmpeg(video_path, output_dir, size=size, stride=stride)


def _collect_videos(path: Path) -> list[Path]:
	"""Return a list of video files to process. Accepts a file or a directory of .mp4 files."""
	if path.is_file():
		return [path]
	if path.is_dir():
		videos = sorted(path.glob("*.mp4"))
		if not videos:
			raise FileNotFoundError(f"No .mp4 files found in directory: {path}")
		return videos
	raise FileNotFoundError(f"Path does not exist: {path}")


def main() -> None:
	parser = argparse.ArgumentParser(description="Extract every Nth center-cropped frame from a video.")
	parser.add_argument(
		"--video",
		required=True,
		type=Path,
		help="Path to the input video file, or a directory containing .mp4 files",
	)
	parser.add_argument(
		"--output",
		type=Path,
		default=Path("datasets/images"),
		help="Output directory for extracted frames (default: datasets/images)",
	)
	parser.add_argument("--size", type=int, default=640, help="Crop size in pixels (square). Default: 640")
	parser.add_argument("--stride", type=int, default=10, help="Save every Nth frame. Default: 10")

	args = parser.parse_args()

	videos = _collect_videos(args.video)
	total_saved = 0
	for vid in videos:
		saved = extract_frames(vid, args.output, size=args.size, stride=args.stride)
		print(f"Saved {saved} frames from {vid} to {args.output}")
		total_saved += saved

	print(f"Total saved: {total_saved} frames to {args.output}")


if __name__ == "__main__":
	main()
