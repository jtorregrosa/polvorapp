import {
  ArrowDown,
  ArrowLeft,
  ArrowRight,
  ArrowUp,
  ImageOff,
  ImagePlus,
  Minus,
  Plus,
  RotateCcw,
  RotateCw,
  Trash2,
  type LucideIcon,
} from 'lucide-react';
import { useCallback, useEffect, useId, useRef, useState, type ChangeEvent } from 'react';
import { useTranslation } from 'react-i18next';
import ReactCrop, { centerCrop, makeAspectCrop, type PercentCrop } from 'react-image-crop';
import 'react-image-crop/dist/ReactCrop.css';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button as ButtonPrimitive } from '@/components/ui/button';
import { cn } from '@/lib/cn';
import { AlertBanner } from './AlertBanner';
import { Button } from './Button';
import { ConfirmDialog } from './ConfirmDialog';
import { PictureTrigger } from './picture-trigger';
import {
  cropImage,
  loadImage,
  MAX_SOURCE_BYTES,
  OversizedImageError,
  PhotoUploadFailure,
  previewOf,
  releaseImage,
  rotateImage,
  UnreadableImageError,
  type ImageOutput,
  type LoadedImage,
  type PixelRegion,
} from './photo-image';

/** Size rules for the cropped photo, in pixels; mirror the server's (spec: Photo validation and processing). */
export interface PhotoRules {
  /** Fixed width-to-height shape, e.g. `3 / 4`; free when omitted. */
  aspect?: number;
  /** Free shapes only: the long side may be at most this many times the short side. */
  maxSideRatio?: number;
  minWidth?: number;
  minHeight?: number;
  /** Minimum length of the longer side. */
  minLongSide?: number;
  maxWidth: number;
  maxHeight: number;
  /** `png` keeps transparency and uploads a PNG (logos); `jpeg` by default (photos). */
  output?: ImageOutput;
}

export interface PhotoRemoval {
  title: string;
  description: string;
  confirmLabel: string;
  onRemove: () => Promise<void>;
}

export interface PhotoUploadProps extends PhotoRules {
  /** Names the photo, e.g. "ID photo"; used in the actions' accessible names. */
  label: string;
  /** The current photo, or null when there is none. */
  photoUrl: string | null;
  /** Describes the current photo, e.g. "ID photo of Ana Pérez". */
  photoAlt: string;
  /**
   * Shown in place of a missing photo, e.g. "No ID photo"; a generic "No photo" by default. Not
   * used by the `picture` variant, whose placeholder is the "Add {label}" button.
   */
  emptyText?: string;
  /** Announced after `onUpload` succeeds; "Saved: <label>" by default. */
  uploadedText?: string;
  /** Announced after a confirmed removal; "Removed: <label>" by default. */
  removedText?: string;
  /**
   * What the image is, for the control's own texts: `photo` (default) or `logo`, whose wording and
   * rules ("at least 256 px", "at most 3 times") differ.
   */
  subject?: 'photo' | 'logo';
  /**
   * Receives the cropped image (JPEG, or PNG with `output="png"`). May reject: a
   * {@link PhotoUploadFailure} shows its (translated) message, anything else a generic one; the
   * crop dialog stays open to try again.
   */
  onUpload: (photo: Blob) => Promise<void>;
  /** Offers removal through a confirmation (spec: Confirmation of destructive actions). */
  removal?: PhotoRemoval;
  disabled?: boolean;
  /** Why the control is disabled, shown under it. */
  disabledHint?: string;
  /** Shows the image only, without choose, replace or remove (e.g. a locked registry). */
  readOnly?: boolean;
  /**
   * `section` (default): the image with add, replace and remove buttons under it (arquebusier
   * photos). `picture`: the image itself is the button, with a replace/remove menu, or the add
   * action when there is none (spec: Picture actions; logos).
   */
  variant?: 'section' | 'picture';
  /**
   * `compact`: a 72 px frame for a record header (the ID photo), its placeholder an icon and a short
   * text; 160 px otherwise.
   */
  size?: 'default' | 'compact';
}

type Status = 'idle' | 'uploading' | 'uploaded' | 'removed';
type RuleProblem = 'tooSmall' | 'aspectRatio';

const INITIAL_CROP_PERCENT = 90;
const PREVIEW_WIDTH = 160;

/** The largest region with the rules' shape inside a `width` × `height` image (all of it when free). */
function largestRegion(width: number, height: number, aspect: number | undefined): PixelRegion {
  if (!aspect) return { x: 0, y: 0, width, height };
  const wide = width / height > aspect;
  return { x: 0, y: 0, width: wide ? height * aspect : width, height: wide ? height : width / aspect };
}

/** Which rule a region breaks, if any. */
function ruleProblem(
  region: Pick<PixelRegion, 'width' | 'height'>,
  rules: PhotoRules,
): RuleProblem | undefined {
  const [long, short] = [Math.max(region.width, region.height), Math.min(region.width, region.height)];
  if (!rules.aspect && rules.maxSideRatio && short > 0 && long / short > rules.maxSideRatio) {
    return 'aspectRatio';
  }
  const bigEnough =
    region.width >= Math.max(1, rules.minWidth ?? 0) &&
    region.height >= Math.max(1, rules.minHeight ?? 0) &&
    long >= (rules.minLongSide ?? 0);
  return bigEnough ? undefined : 'tooSmall';
}

/** Whether the image can give a valid crop as it is or after a quarter turn. */
function imageProblem(image: LoadedImage, rules: PhotoRules): RuleProblem | undefined {
  const upright = ruleProblem(largestRegion(image.width, image.height, rules.aspect), rules);
  const turned = ruleProblem(largestRegion(image.height, image.width, rules.aspect), rules);
  return upright && turned ? upright : undefined;
}

/** The crop in the image's own pixels, kept inside the image despite rounding. */
function toPixels(crop: PercentCrop, image: LoadedImage): PixelRegion {
  const x = Math.min(image.width - 1, Math.max(0, Math.round((crop.x / 100) * image.width)));
  const y = Math.min(image.height - 1, Math.max(0, Math.round((crop.y / 100) * image.height)));
  return {
    x,
    y,
    width: Math.min(image.width - x, Math.round((crop.width / 100) * image.width)),
    height: Math.min(image.height - y, Math.round((crop.height / 100) * image.height)),
  };
}

/** The output size: the crop scaled down to fit the maxima, keeping the fixed shape. */
function outputSize(region: PixelRegion, rules: PhotoRules): { width: number; height: number } {
  const scale = Math.min(1, rules.maxWidth / region.width, rules.maxHeight / region.height);
  const width = Math.round(region.width * scale);
  const height = rules.aspect ? Math.round(width / rules.aspect) : Math.round(region.height * scale);
  return { width, height };
}

function initialCrop(image: LoadedImage, aspect: number | undefined): PercentCrop {
  if (!aspect) return { unit: '%', x: 0, y: 0, width: 100, height: 100 };
  return centerCrop(
    makeAspectCrop({ unit: '%', width: INITIAL_CROP_PERCENT }, aspect, image.width, image.height),
    image.width,
    image.height,
  );
}

/** One press of a crop button moves or resizes the crop by this share of the image (SC 2.5.7). */
const CROP_STEP_PERCENT = 5;
/** The crop never gets smaller than this share of the image's width. */
const MIN_CROP_PERCENT = 10;

const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));

/** The crop moved by a step, kept inside the image. */
function moveCrop(crop: PercentCrop, dx: number, dy: number): PercentCrop {
  return {
    ...crop,
    x: clamp(crop.x + dx * CROP_STEP_PERCENT, 0, 100 - crop.width),
    y: clamp(crop.y + dy * CROP_STEP_PERCENT, 0, 100 - crop.height),
  };
}

/**
 * The crop made a step larger or smaller around its centre, keeping a fixed shape when the screen
 * asks for one, and kept inside the image.
 */
function resizeCrop(crop: PercentCrop, direction: 1 | -1, image: LoadedImage, aspect?: number): PercentCrop {
  // In percentages a fixed shape is not 1:1: height % = width % × image width / (aspect × image height).
  const ratio = aspect ? image.width / (aspect * image.height) : crop.height / crop.width;
  const widest = Math.min(100, 100 / ratio);
  const width = clamp(crop.width + direction * CROP_STEP_PERCENT, MIN_CROP_PERCENT, widest);
  const height = width * ratio;
  return {
    unit: '%',
    width,
    height,
    x: clamp(crop.x + (crop.width - width) / 2, 0, 100 - width),
    y: clamp(crop.y + (crop.height - height) / 2, 0, 100 - height),
  };
}

/** The translation key for why a chosen file could not be opened. */
function loadProblem(error: unknown): 'tooLarge' | 'unsupportedFormat' | 'generic' {
  if (error instanceof OversizedImageError) return 'tooLarge';
  if (error instanceof UnreadableImageError) return 'unsupportedFormat';
  return 'generic';
}

/**
 * Shows a photo and lets the user choose, crop, rotate and upload a new one, or remove it (spec:
 * Photo upload with cropping). The file is checked and cropped in the browser; only the cropped
 * image is handed to `onUpload`, never the original file. Phones offer the camera and the gallery.
 * With `output="png"` transparency is kept from decoding to upload and shown on a checkerboard.
 */
export function PhotoUpload({
  label,
  photoUrl,
  photoAlt,
  emptyText,
  uploadedText,
  removedText,
  subject = 'photo',
  onUpload,
  removal,
  disabled = false,
  disabledHint,
  readOnly = false,
  variant = 'section',
  size = 'default',
  ...rules
}: PhotoUploadProps) {
  const { t } = useTranslation('ui');
  const logo = subject === 'logo';
  const errorText = (reason: RuleProblem | ReturnType<typeof loadProblem>): string =>
    logo ? t(`photoUpload.logo.errors.${reason}`) : t(`photoUpload.errors.${reason}`);
  const copy = logo
    ? {
        use: t('photoUpload.logo.use'),
        cropImage: t('photoUpload.logo.cropImage'),
        preview: t('photoUpload.logo.preview'),
        uploading: t('photoUpload.logo.uploading'),
        loadFailed: t('photoUpload.logo.loadFailed'),
      }
    : {
        use: t('photoUpload.use'),
        cropImage: t('photoUpload.cropImage'),
        preview: t('photoUpload.preview'),
        uploading: t('photoUpload.uploading'),
        loadFailed: t('photoUpload.loadFailed'),
      };
  const fileInput = useRef<HTMLInputElement>(null);
  const chooseButton = useRef<HTMLButtonElement>(null);
  /**
   * Focus is meant to be on the add/replace action. The picture variant's button is replaced when
   * the picture appears or goes away (often after the owner's refetch, once a dialog has closed):
   * the new button then takes the focus the old one lost, unless the user has moved on.
   */
  const keepFocus = useRef(false);
  const focusChooser = () => {
    keepFocus.current = true;
    chooseButton.current?.focus();
  };
  const pictureButton = useCallback((element: HTMLButtonElement | null) => {
    chooseButton.current = element;
    const lost = document.activeElement === null || document.activeElement === document.body;
    if (element && keepFocus.current && lost) element.focus();
  }, []);
  const hintId = useId();
  const [image, setImage] = useState<LoadedImage>();
  const [preview, setPreview] = useState<LoadedImage>();
  const [crop, setCrop] = useState<PercentCrop>();
  const [problem, setProblem] = useState<string>();
  const [cropProblem, setCropProblem] = useState<string>();
  const [status, setStatus] = useState<Status>('idle');
  /** The photo URL that failed to load; a new URL (a replaced photo) is tried again. */
  const [failedUrl, setFailedUrl] = useState<string | null>(null);
  const [working, setWorking] = useState(false);
  const [opening, setOpening] = useState(false);
  /** The removal confirmation of the picture variant, opened from its menu. */
  const [confirmingRemoval, setConfirmingRemoval] = useState(false);
  /** Set when an upload succeeded: announced once the dialog has closed (a closing dialog hides the page from screen readers). */
  const uploaded = useRef(false);
  const loadFailed = photoUrl !== null && failedUrl === photoUrl;
  const output = rules.output ?? 'jpeg';
  const transparencyClass = output === 'png' ? 'bg-checkerboard' : undefined;

  // Images decoded after the control went away are released at once.
  const mounted = useRef(true);
  const previewRequest = useRef(0);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);
  useEffect(() => {
    return () => {
      releaseImage(image);
    };
  }, [image]);
  useEffect(() => {
    return () => {
      releaseImage(preview);
    };
  }, [preview]);

  const close = () => {
    setImage(undefined);
    setPreview(undefined);
    setCrop(undefined);
    setCropProblem(undefined);
  };

  /** Shows the cropped result before it is used (spec: preview the result before confirming). */
  const showPreview = async (source: LoadedImage, region: PercentCrop) => {
    const request = ++previewRequest.current;
    try {
      const next = await previewOf(source, toPixels(region, source), PREVIEW_WIDTH, output);
      // A quicker later request may have answered first: only the latest crop is shown.
      if (mounted.current && request === previewRequest.current) setPreview(next);
      else releaseImage(next);
    } catch {
      // A missing preview does not stop the crop; "Use photo" reports real failures. An old
      // preview must not stay as if it matched the new crop.
      if (mounted.current && request === previewRequest.current) setPreview(undefined);
    }
  };

  const choose = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file || opening) return;
    setProblem(undefined);
    setStatus('idle');
    if (file.size > MAX_SOURCE_BYTES) {
      setProblem(errorText('tooLarge'));
      return;
    }
    setOpening(true);
    let loaded: LoadedImage;
    try {
      loaded = await loadImage(file, output);
    } catch (error) {
      if (mounted.current) {
        setProblem(errorText(loadProblem(error)));
        setOpening(false);
      }
      return;
    }
    if (!mounted.current) {
      releaseImage(loaded);
      return;
    }
    setOpening(false);
    const broken = imageProblem(loaded, rules);
    if (broken) {
      releaseImage(loaded);
      setProblem(errorText(broken));
      return;
    }
    const start = initialCrop(loaded, rules.aspect);
    setImage(loaded);
    setCrop(start);
    void showPreview(loaded, start);
  };

  const adjustCrop = (change: (current: PercentCrop, source: LoadedImage) => PercentCrop) => {
    if (!image || !crop) return;
    const next = change(crop, image);
    setCrop(next);
    setCropProblem(undefined);
    void showPreview(image, next);
  };

  const cropTools: {
    id: string;
    icon: LucideIcon;
    change: (current: PercentCrop, source: LoadedImage) => PercentCrop;
  }[] = [
    { id: 'up', icon: ArrowUp, change: (current) => moveCrop(current, 0, -1) },
    { id: 'down', icon: ArrowDown, change: (current) => moveCrop(current, 0, 1) },
    { id: 'left', icon: ArrowLeft, change: (current) => moveCrop(current, -1, 0) },
    { id: 'right', icon: ArrowRight, change: (current) => moveCrop(current, 1, 0) },
    {
      id: 'smaller',
      icon: Minus,
      change: (current, source) => resizeCrop(current, -1, source, rules.aspect),
    },
    { id: 'larger', icon: Plus, change: (current, source) => resizeCrop(current, 1, source, rules.aspect) },
  ];

  const rotate = async (direction: 1 | -1) => {
    if (!image) return;
    setWorking(true);
    setCropProblem(undefined);
    try {
      const rotated = await rotateImage(image, direction, output);
      if (!mounted.current) {
        releaseImage(rotated);
        return;
      }
      const start = initialCrop(rotated, rules.aspect);
      setImage(rotated);
      setCrop(start);
      void showPreview(rotated, start);
    } catch {
      setCropProblem(errorText('generic'));
    } finally {
      if (mounted.current) setWorking(false);
    }
  };

  /** Crops and uploads; the dialog stays open until the upload succeeds, so a failure can be retried. */
  const use = async () => {
    if (!image || !crop) return;
    const region = toPixels(crop, image);
    const broken = ruleProblem(region, rules);
    if (broken) {
      setCropProblem(errorText(broken));
      return;
    }
    setWorking(true);
    setCropProblem(undefined);
    try {
      const size = outputSize(region, rules);
      const photo = await cropImage(image, region, size.width, size.height, output);
      setStatus('uploading');
      await onUpload(photo);
      if (!mounted.current) return;
      uploaded.current = true;
      setStatus('idle');
      close();
    } catch (error) {
      if (!mounted.current) return;
      setStatus('idle');
      setCropProblem(error instanceof PhotoUploadFailure ? error.message : errorText('generic'));
    } finally {
      if (mounted.current) setWorking(false);
    }
  };

  const uploading = status === 'uploading';
  const announcement =
    status === 'uploaded'
      ? (uploadedText ?? t('photoUpload.uploaded', { label }))
      : status === 'removed'
        ? (removedText ?? t('photoUpload.removed', { label }))
        : '';

  const picture = variant === 'picture' && !readOnly;
  const pickFile = () => fileInput.current?.click();
  /** The image, or a placeholder saying why there is none; inside the picture button the image is decorative. */
  const compact = size === 'compact';
  const content = (emptyLabel: string, EmptyIcon = ImageOff, hiddenLabel?: string) =>
    photoUrl && !loadFailed ? (
      <img
        src={photoUrl}
        alt={picture ? '' : photoAlt}
        className="h-auto w-full"
        onError={() => {
          setFailedUrl(photoUrl);
        }}
      />
    ) : (
      <span
        className={cn(
          'flex flex-col items-center justify-center text-center text-muted-foreground',
          compact ? 'min-h-24 gap-1 p-1 text-help break-words hyphens-auto' : 'min-h-32 gap-2 p-3 text-sm',
        )}
      >
        {photoUrl ? (
          <ImageOff aria-hidden="true" className={compact ? 'size-5' : 'size-6'} />
        ) : (
          <EmptyIcon aria-hidden="true" className={compact ? 'size-5' : 'size-6'} />
        )}
        <span>{photoUrl ? copy.loadFailed : emptyLabel}</span>
        {hiddenLabel && (
          <>
            {' '}
            <span className="sr-only">{hiddenLabel}</span>
          </>
        )}
      </span>
    );

  return (
    <div className="flex flex-col gap-3">
      {picture ? (
        <PictureTrigger
          ref={pictureButton}
          frameClassName={transparencyClass}
          compact={compact}
          // A picture that failed to load is named by what it shows first (WCAG 2.5.3).
          menuName={
            photoUrl
              ? `${loadFailed ? `${copy.loadFailed}. ` : ''}${t('pictureActions.options', { label: photoAlt })}`
              : undefined
          }
          canRemove={removal !== undefined}
          disabled={disabled}
          busy={opening}
          describedBy={disabled && disabledHint ? hintId : undefined}
          onReplace={pickFile}
          onRemove={() => {
            setConfirmingRemoval(true);
          }}
          onFocusMovedAway={() => {
            keepFocus.current = false;
          }}
        >
          {/* Compact, the frame says what is missing ("No ID photo") and the button's name adds the action. */}
          {compact
            ? content(
                emptyText ?? t('photoUpload.empty', { label }),
                ImagePlus,
                t('pictureActions.add', { label }),
              )
            : content(t('pictureActions.add', { label }), ImagePlus)}
        </PictureTrigger>
      ) : (
        <div
          className={cn(
            'flex items-center justify-center overflow-hidden rounded-md border',
            compact ? 'w-18' : 'w-40',
            // A transparent logo is judged on the checkerboard, as in the crop dialog.
            transparencyClass ?? 'bg-muted',
          )}
        >
          {content(emptyText ?? t('photoUpload.empty', { label }))}
        </div>
      )}
      {picture && removal && (
        <ConfirmDialog
          open={confirmingRemoval}
          onOpenChange={setConfirmingRemoval}
          title={removal.title}
          description={removal.description}
          confirmLabel={removal.confirmLabel}
          onConfirm={removal.onRemove}
          returnFocus={chooseButton}
          onConfirmed={() => {
            // Back to the picture, which becomes the add action once the logo is gone (WCAG 2.4.3).
            focusChooser();
            setStatus('removed');
          }}
        />
      )}

      {/* Long labels ("Replace front of the license") wrap inside narrow columns (WCAG 1.4.10). */}
      {!readOnly && !picture && (
        <div className="flex min-w-0 flex-wrap gap-2">
          <Button
            ref={chooseButton}
            type="button"
            variant="secondary"
            className="h-auto min-h-control max-w-full py-2 text-left whitespace-normal"
            pending={opening}
            disabled={disabled}
            aria-describedby={disabled && disabledHint ? hintId : undefined}
            // With a photo, the short verb is shown and the full name ("Replace front of the license") read.
            aria-label={photoUrl ? t('photoUpload.replace', { label }) : undefined}
            onClick={pickFile}
          >
            {photoUrl ? t('pictureActions.replace') : t('photoUpload.choose', { label })}
          </Button>
          {removal && photoUrl && (
            <ConfirmDialog
              title={removal.title}
              description={removal.description}
              confirmLabel={removal.confirmLabel}
              onConfirm={removal.onRemove}
              // The remove action goes away with the photo: focus the add action instead (WCAG 2.4.3).
              onConfirmed={() => {
                chooseButton.current?.focus();
                // Announced politely: the remove action went away with the photo (SC 4.1.3).
                setStatus('removed');
              }}
              trigger={
                <Button
                  type="button"
                  variant="quietDestructive"
                  icon={Trash2}
                  disabled={disabled}
                  aria-label={t('photoUpload.remove', { label })}
                  className="h-auto min-h-control max-w-full py-2 text-left whitespace-normal"
                >
                  {t('pictureActions.remove')}
                </Button>
              }
            />
          )}
        </div>
      )}
      {/* No `capture`: phones then offer both the camera and the gallery (NFR-01). */}
      {!readOnly && (
        <input
          ref={fileInput}
          type="file"
          accept="image/*"
          hidden
          onChange={(event) => {
            void choose(event);
          }}
        />
      )}

      {disabled && disabledHint && (
        <p id={hintId} className="text-sm text-muted-foreground">
          {disabledHint}
        </p>
      )}
      {problem && <AlertBanner severity="error">{problem}</AlertBanner>}
      <p role="status" className="sr-only">
        {announcement}
      </p>

      <Dialog
        open={image !== undefined}
        onOpenChange={(open) => {
          if (!open && !working) close();
        }}
      >
        <DialogContent
          // Scrolls on short screens (phones in landscape, high zoom), so every action stays reachable;
          // the single grid column never grows past the dialog (320 px screens).
          className="max-h-dvh grid-cols-1 overflow-y-auto"
          // Opened from the file picker, not from a button: return focus to the add action.
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            focusChooser();
            if (uploaded.current) {
              uploaded.current = false;
              setStatus('uploaded');
            }
          }}
        >
          <DialogHeader>
            <DialogTitle>{t('photoUpload.cropTitle', { label })}</DialogTitle>
            <DialogDescription>{t('photoUpload.cropHelp')}</DialogDescription>
          </DialogHeader>
          {image && (
            <div className="flex flex-col items-center gap-4 sm:flex-row sm:items-start">
              <ReactCrop
                crop={crop}
                aspect={rules.aspect}
                keepSelection
                ruleOfThirds
                disabled={working}
                className="max-h-64 sm:max-h-80"
                ariaLabels={{
                  cropArea: t('photoUpload.cropArea'),
                  nwDragHandle: t('photoUpload.handles.nw'),
                  nDragHandle: t('photoUpload.handles.n'),
                  neDragHandle: t('photoUpload.handles.ne'),
                  eDragHandle: t('photoUpload.handles.e'),
                  seDragHandle: t('photoUpload.handles.se'),
                  sDragHandle: t('photoUpload.handles.s'),
                  swDragHandle: t('photoUpload.handles.sw'),
                  wDragHandle: t('photoUpload.handles.w'),
                }}
                onChange={(_, percent) => {
                  setCrop(percent);
                  setCropProblem(undefined);
                }}
                onComplete={(_, percent) => {
                  void showPreview(image, percent);
                }}
              >
                <img
                  src={image.url}
                  alt={copy.cropImage}
                  className={cn('max-h-64 sm:max-h-80', transparencyClass)}
                />
              </ReactCrop>
              {preview && (
                <figure className="flex w-28 flex-col gap-1 text-center text-xs text-muted-foreground">
                  <img
                    src={preview.url}
                    alt={copy.preview}
                    className={cn('h-auto w-full rounded-sm border', transparencyClass)}
                  />
                  <figcaption>{t('photoUpload.previewCaption')}</figcaption>
                </figure>
              )}
            </div>
          )}
          {image && (
            // Moving and resizing without dragging (SC 2.5.7); the arrow keys also work on the crop.
            <div role="group" aria-label={t('photoUpload.cropTools.label')} className="flex flex-wrap gap-1">
              {cropTools.map(({ id, icon: Icon, change }) => (
                <ButtonPrimitive
                  key={id}
                  type="button"
                  variant="outline"
                  size="icon"
                  disabled={working}
                  aria-label={t(`photoUpload.cropTools.${id}` as 'photoUpload.cropTools.up')}
                  title={t(`photoUpload.cropTools.${id}` as 'photoUpload.cropTools.up')}
                  onClick={() => {
                    adjustCrop(change);
                  }}
                >
                  <Icon aria-hidden="true" />
                </ButtonPrimitive>
              ))}
            </div>
          )}
          {cropProblem && <AlertBanner severity="error">{cropProblem}</AlertBanner>}
          {/* Inside the dialog: the page behind it is hidden from screen readers while it is open. */}
          <p role="status" className="sr-only">
            {uploading ? copy.uploading : ''}
          </p>
          <DialogFooter className="flex-col gap-2 sm:flex-row sm:flex-wrap sm:justify-between">
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                variant="secondary"
                icon={RotateCcw}
                pending={working}
                onClick={() => {
                  void rotate(-1);
                }}
              >
                {t('photoUpload.rotateLeft')}
              </Button>
              <Button
                type="button"
                variant="secondary"
                icon={RotateCw}
                pending={working}
                onClick={() => {
                  void rotate(1);
                }}
              >
                {t('photoUpload.rotateRight')}
              </Button>
            </div>
            <div className="flex flex-wrap gap-2">
              <Button type="button" variant="quiet" pending={working} onClick={close}>
                {t('photoUpload.cancel')}
              </Button>
              <Button
                type="button"
                pending={working}
                onClick={() => {
                  void use();
                }}
              >
                {copy.use}
              </Button>
            </div>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
