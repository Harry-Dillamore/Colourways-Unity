# Technical Commentary — FMOD Studio Audio Middleware Integration

I integrated FMOD Studio 2.03.15 and FMOD for Unity 2.03.15 into the ColourWays project built in Unity. I disabled Unity's default AudioManager to avoid conflict with FMOD's audio engine. I replaced Unity's `AudioListener` components with FMOD's `StudioListener` components, with the player's attenuation object set as the player, so that sound distance attenuation is based on the player's position, rather than the position of the camera, as we use a very high and far angle for the game - which would make sounds very quiet and distant. I looked into how to set up FMOD calls, and found that you can do it by putting them directly into scripts, but this can become messy by combining audio and gameplay logic. Instead, the recommended approach, and the one I used, is to broadcast an event from the gameplay script when something happens that should play a sound, and a central audio manager listens for the events and plays the FMOD sound. Additionally, the `ColourWaysAudioManager` stays persistent across scene loads, meaning that the background music does not abruptly cut off or restart when loading a new scene.

I created 10 FMOD events in the `Master` bank, including: MainTheme, Footstep, Hover, Respawn, and more. I was able to implement the main theme music to play on a loop, and apply a low-pass filter to the music when the game is paused, which gives extra audio feedback to the player. I also added sound effects such as footsteps which randomised between 5 variations in order to stop the sound from becoming repetitive as it is played frequently - using a Multi Instrument within FMOD Studio was useful here to add variations without any extra code. The in-world gameplay SFX are 3D spatialised to create a more immersive gameplay experience, while the UI and feedback SFX are 2D. To confirm that the audio was working as intended, I used a custom `AudioTelemetry` logger to log FMOD.RESULT to confirm that the sounds are being triggered correctly.

I built a standalone Windows build of the game and ran the `AudioDebugOverlay` and played through the game, testing all of the sounds, and watching the debug overlay to see if there were any errors. Doing this in a build allowed me to catch errors that do not come up in editor.

![FMOD Events](image.png)

![footstep](image-1.png)

AI Declaration

Gemini Neon was used to help generate the FMOD code and documentation.
