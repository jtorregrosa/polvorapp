# Spec Delta

## MODIFIED Requirements

### Requirement: Photo upload with cropping
The composite layer SHALL offer a photo upload control for feature screens. It SHALL show the
current photo with a text alternative that describes it, or an empty state when there is none. It
SHALL let the user:
- choose an image file;
- on a phone, take a picture with the camera or pick one from the gallery (NFR-01);
- crop the image in a dialog, with a fixed shape when the screen asks for one;
- rotate the image by quarter turns;
- preview the result before confirming.

Without a fixed shape, the crop area SHALL start with the whole image selected. When the screen asks
to keep transparency (for example for logos), the control SHALL keep the transparent areas of the
image through decoding, rotation, cropping and the preview, and SHALL upload a PNG; otherwise it
SHALL upload a JPEG.

It SHALL check the format, the size and the minimum dimensions that the screen asks for before
anything is uploaded, and SHALL explain any problem in the user's language. It SHALL upload only
the cropped image, never the original file, and SHALL announce when the upload is in progress, has
succeeded or has failed. Every part SHALL be operable with the keyboard alone, including moving and
resizing the crop area. Moving and resizing the crop area SHALL also be possible with single
pointer activations (buttons), without dragging (SC 2.5.7). The control SHALL meet the "Accessible
composites" requirement. A photo that cannot be loaded SHALL be replaced by a message, never a
broken image. Removing a photo SHALL follow the "Confirmation of destructive actions" requirement.

#### Scenario: Crop with the keyboard
- **WHEN** a keyboard user opens the crop dialog for a 3:4 photo, moves and resizes the crop area with the arrow keys, and confirms
- **THEN** the cropped 3:4 image is handed to the screen and focus returns to the control that opened the dialog

#### Scenario: Crop without dragging
- **WHEN** a user moves the crop area and makes it smaller using only the dialog's buttons
- **THEN** the crop area moves and shrinks by one step per activation, keeping its fixed shape, and the result can be confirmed

#### Scenario: Free crop starts with the whole image
- **WHEN** a user chooses an image for a control without a fixed shape
- **THEN** the crop dialog opens with the whole image selected, and confirming at once hands the whole image to the screen

#### Scenario: Transparency kept
- **WHEN** a user chooses a PNG with a transparent background for a control that keeps transparency, rotates it and confirms
- **THEN** the screen receives a PNG whose background is still transparent, and the preview shows the transparency

#### Scenario: Image too small
- **WHEN** a user chooses an image smaller than the minimum dimensions the screen asks for
- **THEN** the control explains that the image is too small, and nothing is uploaded

#### Scenario: Unreadable file
- **WHEN** a user chooses a file that the browser cannot open as an image
- **THEN** the control explains that the format is not supported, and nothing is uploaded

#### Scenario: Photo fails to load
- **WHEN** the current photo cannot be loaded
- **THEN** the control shows a translated message in its place instead of a broken image

#### Scenario: Catalogue entry
- **WHEN** the component catalogue is built
- **THEN** it shows the photo upload control empty, with a photo, with a transparent logo, disabled with its reason and with an error, in both themes and the three UI languages
