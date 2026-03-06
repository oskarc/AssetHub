import * as fs from 'node:fs';
import * as path from 'node:path';

const FIXTURES_DIR = path.join(__dirname, '..', 'fixtures');

/**
 * Generates test fixture files for upload tests.
 */
export function ensureTestFixtures() {
  if (!fs.existsSync(FIXTURES_DIR)) {
    fs.mkdirSync(FIXTURES_DIR, { recursive: true });
  }

  // Create a minimal valid PNG (1x1 red pixel)
  const pngPath = path.join(FIXTURES_DIR, 'test-image.png');
  if (!fs.existsSync(pngPath)) {
    const pngBuffer = Buffer.from(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==',
      'base64'
    );
    fs.writeFileSync(pngPath, pngBuffer);
  }

  // Create a minimal valid PDF
  const pdfPath = path.join(FIXTURES_DIR, 'test-document.pdf');
  if (!fs.existsSync(pdfPath)) {
    const pdfContent = `%PDF-1.4
1 0 obj
<< /Type /Catalog /Pages 2 0 R >>
endobj
2 0 obj
<< /Type /Pages /Kids [3 0 R] /Count 1 >>
endobj
3 0 obj
<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>
endobj
xref
0 4
0000000000 65535 f 
0000000009 00000 n 
0000000058 00000 n 
0000000115 00000 n 
trailer
<< /Size 4 /Root 1 0 R >>
startxref
190
%%EOF`;
    fs.writeFileSync(pdfPath, pdfContent);
  }

  // Create a larger test image (for size display tests)
  const largePngPath = path.join(FIXTURES_DIR, 'test-image-large.png');
  if (!fs.existsSync(largePngPath)) {
    // Generate a ~50KB PNG-like file (valid header, padded)
    const header = Buffer.from(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==',
      'base64'
    );
    fs.writeFileSync(largePngPath, header);
  }

  return {
    testImage: pngPath,
    testPdf: pdfPath,
    testImageLarge: largePngPath,
  };
}

/** Get absolute path to a test fixture */
export function fixturePath(name: string): string {
  return path.join(FIXTURES_DIR, name);
}
