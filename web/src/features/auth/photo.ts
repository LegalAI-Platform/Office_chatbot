// The account's photo (migration 0036): whatever is picked goes up as a small square JPEG.

/** What the API takes (it checks the bytes too); anything else is refused before any upload. */
export const PHOTO_TYPES = ["image/jpeg", "image/png", "image/webp"];

/**
 * The middle square of the picture, 256×256, as a JPEG (quality 0.85): a few dozen KB, far under the API's
 * 300 KB. createImageBitmap reads the file without a blob: address (the panel's CSP shows only its own and
 * data: images) and turns a phone photo the right way up.
 */
export async function squareJpeg(file: Blob, size = 256): Promise<Blob> {
  const bitmap = await createImageBitmap(file);
  try {
    const side = Math.min(bitmap.width, bitmap.height);
    const canvas = document.createElement("canvas");
    canvas.width = size;
    canvas.height = size;
    const ctx = canvas.getContext("2d");
    if (!ctx || side === 0) throw new Error("no canvas");
    ctx.fillStyle = "#ffffff"; // a transparent PNG gets white, not black, behind it
    ctx.fillRect(0, 0, size, size);
    ctx.imageSmoothingQuality = "high";
    ctx.drawImage(bitmap, (bitmap.width - side) / 2, (bitmap.height - side) / 2, side, side, 0, 0, size, size);
    return await new Promise<Blob>((resolve, reject) =>
      canvas.toBlob((b) => (b ? resolve(b) : reject(new Error("no jpeg"))), "image/jpeg", 0.85));
  } finally {
    bitmap.close();
  }
}
