# Crowd Control Unity Package

This Unity package provides integration with the Crowd Control platform, allowing developers to easily add interactive features to their Unity games.

## Who is this for?

This package is intended for Unity developers who want to integrate Crowd Control functionality into their games. It is suitable for both beginners and experienced developers looking to enhance player engagement through interactive features.

Crowd Control support is available for a variety of platforms.
If you are working on a non-Unity project, please contact Warp World for code samples and support for your platform of choice.

## Installation

To install the Crowd Control Unity package, follow these steps:
1. Open your Unity project.
2. Go to `Window` > `Package Manager`.
3. Click on the `+` button in the top left corner and select `Add package from git URL...`.
4. Enter the following URL: `https://github.com/WarpWorld/CrowdControl.Client.Unity.Package.git`.
5. Click `Add` to install the package.
6. Once installed, you can find the Crowd Control components in the `Packages` section of the Package Manager.

## Initial Setup

To use the Crowd Control package in your Unity project, follow these steps:
1. Create a new GameObject in your scene.
2. Add the `CrowdControlBehavior` component to the GameObject.
3. Configure the `CrowdControlBehavior` with your [Crowd Control game ID, app ID, and app secret](https://developer.crowdcontrol.live/sockets/#authenticating).
4. Create a script that inherits from `GameStateManager` and assign it to a GameObject in your scene.
5. Assign the `GameStateManager` instance to the `CrowdControlBehavior` component.
6. Implement the desired Crowd Control effects in your game by creating classes that inherit from `CrowdControlEffect`.
7. Create a new GameObject and add the `UnityEffectLoader` component to it.

## Creating Effects

To create custom Crowd Control effects in your Unity project, follow these steps:
1. Create a new C# script that inherits from `CrowdControlEffect`.
2. Override the necessary methods to define the behavior of your effect.
3. Create a new GameObject as a child of the `UnityEffectLoader` GameObject.
4. Add the new effect script to the GameObject.
5. Configure the effect parameters as needed.
6. Repeat the process for each effect you want to add to your game.
