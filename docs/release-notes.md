# ARCYN 1.6.0-rc.1

Release candidate for Windows 10/11 x64. Product branding remains ARCYN.

- Shared material composition exposes the native backdrop only after successful initialization. Transient surfaces request Acrylic; workspace/settings surfaces request Mica. Unsupported composition, reduced effects, high contrast and unsuitable custom palettes receive an opaque treatment. No per-card blur or new rendering loop.
- Visible workspace creation, appearance access and card menus; a useful empty state; retained cancellation/failure guidance. Optional editor settings use disclosures, narrow inspectors stack, and monitor captions sit outside saved window rectangles.
- Common appearance controls come first, layouts have readable names, and invalid inputs identify the field. Saved customization, configuration schema, exact commands, command review, session ownership and cinematic durations remain intact.
- Consistent release metadata and original ARCYN icon across executable, windows, helper and generated shortcuts. Complete self-contained .NET/WPF 10.0.13 distribution includes font and runtime notices; package checks reject development artifacts and mismatched metadata.
- Live measurement probe detaches its rendering callback at Ready. Initial sequential runs are no longer described as extraction/cold-start measurements.

Current evidence is headless. Offscreen renders show layout and opaque material policy, not native desktop glass. Live GUI, assistive navigation, real application orchestration, mixed-DPI layout, focus/UIPI/UAC, cinematic playback and whole-app resource cleanup remain release gates. No master merge or public release is included.
