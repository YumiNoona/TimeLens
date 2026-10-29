import { assetRedirectUrl, getLatestRelease, installerAssetName } from '../server/github-release.js';

export default async function handler(request, response) {
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    response.setHeader('Allow', 'GET, HEAD');
    return response.status(405).json({ error: 'Method not allowed' });
  }

  const platforms = { windows: installerAssetName(), android: 'TimeLens-Android.apk',
    bundle: 'TimeLens-Android.aab', chrome: 'TimeLens-Chrome-Extension.zip',
    firefox: 'TimeLens-Firefox-Extension.zip', edge: 'TimeLens-Edge-Extension.zip', brave: 'TimeLens-Brave-Extension.zip' };
  const platform = request.query?.platform || 'windows';
  if (typeof platform !== 'string' || !Object.hasOwn(platforms, platform))
    return response.status(400).json({ error: 'Choose a supported platform.' });
  try {
    const { asset } = await getLatestRelease(platforms[platform]);
    const downloadUrl = await assetRedirectUrl(asset);
    response.setHeader('Cache-Control', 'private, no-store');
    response.setHeader('Location', downloadUrl);
    return response.status(302).end();
  } catch (error) {
    console.error('Release download failed:', error);
    response.setHeader('Cache-Control', 'no-store');
    return response.status(503).json({ error: 'The TimeLens download is temporarily unavailable.' });
  }
}
