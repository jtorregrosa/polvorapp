# Synthetic seed images

Images used only by the synthetic seed (`seed` host command, SEC-11). They are copied to the API's
output as `SyntheticData/` and read by the seeders. Nothing else in the application uses them.

| Folder | Content |
|---|---|
| `faces/` | 400 ID photos of people who **do not exist**. JPEG 600 × 800 (3:4, NFR-15). `manifest.json` gives each face's gender, age band, age and seed. |
| `logos/` | Invented heraldic emblems, one per seeded comparsa except Abencerrajes. Transparent PNG, at most 512 px. `manifest.json` gives each emblem's comparsa, seed and prompt. |

## How they were made

- **Generator:** Z-Image Turbo by Tongyi-MAI, through Runpod's public endpoint.
  - The model weights are under the [Apache License 2.0](https://huggingface.co/Tongyi-MAI/Z-Image-Turbo), which places no
    restriction on generated output.
  - The prompts and seeds are in `scripts/generate-seed-images.mjs` and in the manifests.
- **Post-processing:** the same script optimises the images with sharp.
  - Faces are resized and re-encoded with mozjpeg.
  - Logos have their background made transparent, are trimmed, resized and palette-encoded.
  - All metadata is removed.

## What they are not

- **No real person.** The faces are not photographs of anyone, nor derived from a photograph of a
  known person, so they are not personal data. A face found to resemble a real person is
  regenerated with another seed.
- **No real emblem.** The logos are not the emblem of any real comparsa, federation or
  organisation (ADR-0013), and they contain no text.

## License

The images are part of PolvorApp and are distributed under the repository's [MIT License](../../LICENSE).
