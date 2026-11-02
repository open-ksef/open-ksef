# ksef-pdf-generator (vendored)

Pinned build of [CIRFMF/ksef-pdf-generator](https://github.com/CIRFMF/ksef-pdf-generator), which is not published on npm.

| | |
|---|---|
| Version | tag `1.1.46`, commit `8a18843db12d0013a417500bb2f98bfe49b7d2a8` |
| File | `ksef-fe-invoice-converter.js` (ES module, unmodified build output) |
| SHA-256 | `551b4ba283db648620c2d03cac9c2f2dd50375b47173c79d6e264c88dad152e4` |
| License | MIT, see `LICENSE`. The bundle also contains pdfmake, i18next and xml-js (MIT) and the Roboto fonts from pdfmake (Apache-2.0). |

Rebuild:

```bash
git clone --depth 1 --branch 1.1.46 https://github.com/CIRFMF/ksef-pdf-generator.git
cd ksef-pdf-generator
npm ci --ignore-scripts
npm run build
cp dist/ksef-fe-invoice-converter.js LICENSE <this folder>
```

Load it only through a dynamic `import()`, so it stays out of the main chunk. Only the export the portal uses is typed in `ksef-fe-invoice-converter.d.ts`.
