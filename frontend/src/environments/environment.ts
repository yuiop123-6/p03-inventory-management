/**
 * 默认环境（ng serve 开发时使用）。
 * 浏览器只访问同源的 /api，由 dev-server 代理（proxy.conf.json）转发到后端
 * http://localhost:5000，避免跨域问题。
 */
export const environment = {
  production: false,
  apiUrl: '/api',
};
