#version 430 core
#extension GL_ARB_bindless_texture : require
// ImGui overlay: the texture is the RGBA font atlas (or another ImGui texture).
in vec2 vUv;
in vec4 vColor;
out vec4 FragColor;

uniform uint uTextureIndexA;

void main() {
    FragColor = ACDREAM_SAMPLE_2D(uTextureIndexA, vUv) * vColor;
}
